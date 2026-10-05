using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.CuentaCorriente;

namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Anula TOTALMENTE una venta confirmada: registra la reversión auditada,
/// crea movimientos de ingreso compensatorios que restituyen el Disponible y
/// marca la venta como anulada, sin borrar ni editar venta, líneas, pagos,
/// movimientos ni auditoría originales. Sin anulación parcial ni asientos contables. Toda la operación comparte
/// transacción y auditoría: si algo falla, no persiste nada.
/// </summary>
public sealed class AnularVenta
{
    private readonly IRepositorioVentas _ventas;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;
    private readonly IRepositorioCambios? _cambios;
    private readonly IRepositorioCuentaCorriente? _cuenta;

    public AnularVenta(
        IRepositorioVentas ventas,
        IRepositorioMovimientos movimientos,
        IRepositorioCaja caja,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
        : this(ventas, movimientos, caja, autorizador, fabrica, auditoria, ids, reloj, null, null) { }

    public AnularVenta(
        IRepositorioVentas ventas,
        IRepositorioMovimientos movimientos,
        IRepositorioCaja caja,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj,
        IRepositorioCambios? cambios,
        IRepositorioCuentaCorriente? cuenta = null)
    {
        _ventas = ventas;
        _movimientos = movimientos;
        _caja = caja;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
        _cambios = cambios;
        _cuenta = cuenta;
    }

    public async Task EjecutarAsync(string ventaId, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosVentas.VentasAnular, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var venta = await _ventas.ObtenerPorIdAsync(sesion, ventaId, cancellationToken);
        if (venta is null)
        {
            throw new InvalidOperationException("La venta no existe.");
        }

        if (await _ventas.ExisteReversionAsync(sesion, ventaId, cancellationToken))
        {
            throw new InvalidOperationException("Esta venta ya fue anulada.");
        }

        if (venta.Estado != "confirmada")
        {
            throw new InvalidOperationException("Solo se puede anular una venta confirmada.");
        }

        if (_cambios is not null && await _cambios.ExistePorVentaAsync(sesion, ventaId, cancellationToken))
            throw new InvalidOperationException("No se puede anular una venta que ya tiene cambios o devoluciones registrados.");

        var lineas = await _ventas.ListarLineasAsync(sesion, ventaId, cancellationToken);
        if (lineas.Count == 0)
        {
            throw new InvalidOperationException("La venta no tiene líneas.");
        }

        var pagos = await _caja.ListarPagosActualesPorVentaAsync(sesion, ventaId, cancellationToken);
        var fecha = _reloj.UtcNow().ToString("o");
        var reintegrosIniciales = new List<(PagoCajaActual Pago, long Importe)>();
        foreach (var pago in pagos)
        {
            var pendiente = pago.ImporteMinor - await _caja.SumarReintegrosPorPagoAsync(sesion, pago.PagoId, cancellationToken);
            if (pendiente > 0) reintegrosIniciales.Add((pago, pendiente));
        }
        var aplicacionesAbono = _cuenta is null
            ? Array.Empty<(CargoCuentaCorriente Cargo, string ComponenteId, string Medio, long Importe)>()
            : await _cuenta.ListarAplicacionesPagadasVentaAsync(sesion, ventaId, cancellationToken);
        var reintegrosAbono = new List<(string ComponenteId, string Medio, long Importe)>();
        foreach (var app in aplicacionesAbono.GroupBy(a => new { a.ComponenteId, a.Medio }).Select(g => (g.Key.ComponenteId, g.Key.Medio, Importe: g.Sum(x => x.Importe))))
        {
            var pendiente = app.Importe - await _cuenta!.SumarReintegrosPorComponenteVentaAsync(sesion, app.ComponenteId, ventaId, cancellationToken);
            if (pendiente > 0) reintegrosAbono.Add((app.ComponenteId, app.Medio, pendiente));
        }
        var cargosVenta = venta.ClienteId is null || _cuenta is null
            ? Array.Empty<CargoCuentaCorriente>()
            : await _cuenta.ListarCargosPendientesVentaAsync(sesion, ventaId, cancellationToken);
        var reintegroTotal = reintegrosIniciales.Sum(x => x.Importe) + reintegrosAbono.Sum(x => x.Importe);
        var cajaAbierta = reintegroTotal > 0
            ? await _caja.ObtenerAbiertaAsync(sesion, cancellationToken) ?? throw new InvalidOperationException("No hay una caja abierta para registrar el reintegro.")
            : null;
        var cajaOperacionId = cajaAbierta is null ? null : await _caja.InsertarOperacionAsync(
            sesion, new NuevaOperacionCaja(operacionId, cajaAbierta.Id, TiposOperacionCaja.Anulacion, actor.Id, fecha), cancellationToken);
        await _ventas.InsertarReversionAsync(sesion, ventaId, operacionId, motivo.Trim(), actor.Id, fecha, cancellationToken);
        foreach (var (pago, pendiente) in reintegrosIniciales)
        {
            await _caja.InsertarMovimientoAsync(sesion,
                new NuevoMovimientoCaja(cajaAbierta!.Id, cajaOperacionId!, TiposMovimientoCaja.Reintegro, -pendiente, ventaId, pago.PagoId, pago.MedioActual, null, motivo.Trim(), actor.Id, pago.MovimientoOriginalId, fecha), cancellationToken);
        }
        foreach (var reembolso in reintegrosAbono)
        {
            await _caja.InsertarMovimientoAsync(sesion,
                new NuevoMovimientoCaja(cajaAbierta!.Id, cajaOperacionId!, TiposMovimientoCaja.Reintegro, -reembolso.Importe, ventaId, null, reembolso.Medio, null, motivo.Trim(), actor.Id, null, fecha, AbonoComponenteId: reembolso.ComponenteId), cancellationToken);
        }
        var deudaPendiente = cargosVenta.Sum(c => c.PendienteMinor);
        if (deudaPendiente > 0)
        {
            var reductor = await _cuenta!.InsertarMovimientoAsync(sesion, new(venta.ClienteId!, TiposMovimientoCuentaCorriente.CancelacionDeuda, -deudaPendiente, operacionId, actor.Id, fecha, VentaId: ventaId, Motivo: motivo.Trim()), cancellationToken);
            var restante = deudaPendiente;
            foreach (var cargo in cargosVenta)
            {
                var aplica = Math.Min(restante, cargo.PendienteMinor);
                if (aplica > 0) await _cuenta.InsertarAplicacionAsync(sesion, reductor, new(cargo.MovimientoId, aplica), fecha, cancellationToken);
                restante -= aplica;
            }
            if (restante != 0) throw new InvalidOperationException("No se pudo asignar la cancelación a la deuda de esta venta.");
        }
        foreach (var linea in lineas)
        {
            await _movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(linea.VarianteId, "ingreso", linea.Cantidad, $"Anulación venta {venta.Numero} ({motivo.Trim()})", operacionId, actor.Id),
                fecha,
                cancellationToken);
        }

        await _ventas.MarcarAnuladaAsync(sesion, ventaId, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "venta_anulada", ActorAuditoria.Usuario, actor.Id, "venta", ventaId, motivo.Trim()),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
