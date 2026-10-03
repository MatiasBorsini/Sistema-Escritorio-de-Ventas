using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;

namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Anula TOTALMENTE una venta confirmada: registra la reversión auditada,
/// crea movimientos de ingreso compensatorios que restituyen el Disponible y
/// marca la venta como anulada, sin borrar ni editar venta, líneas, pagos,
/// movimientos ni auditoría originales. Sin anulación parcial, sin
/// devolución parcial, sin asientos contables. Toda la operación comparte
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

    public AnularVenta(
        IRepositorioVentas ventas,
        IRepositorioMovimientos movimientos,
        IRepositorioCaja caja,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _ventas = ventas;
        _movimientos = movimientos;
        _caja = caja;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
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

        var lineas = await _ventas.ListarLineasAsync(sesion, ventaId, cancellationToken);
        if (lineas.Count == 0)
        {
            throw new InvalidOperationException("La venta no tiene líneas.");
        }

        var cajaAbierta = await _caja.ObtenerAbiertaAsync(sesion, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta para registrar el reintegro.");
        var pagos = await _caja.ListarPagosActualesPorVentaAsync(sesion, ventaId, cancellationToken);
        if (pagos.Count == 0)
            throw new InvalidOperationException("La venta no tiene pagos para compensar en caja.");

        var fecha = _reloj.UtcNow().ToString("o");
        var cajaOperacionId = await _caja.InsertarOperacionAsync(
            sesion,
            new NuevaOperacionCaja(operacionId, cajaAbierta.Id, TiposOperacionCaja.Anulacion, actor.Id, fecha),
            cancellationToken);
        await _ventas.InsertarReversionAsync(sesion, ventaId, operacionId, motivo.Trim(), actor.Id, fecha, cancellationToken);
        foreach (var pago in pagos)
        {
            var reintegrado = await _caja.SumarReintegrosPorPagoAsync(sesion, pago.PagoId, cancellationToken);
            var pendiente = pago.ImporteMinor - reintegrado;
            if (pendiente > 0)
            {
                await _caja.InsertarMovimientoAsync(
                    sesion,
                    new NuevoMovimientoCaja(cajaAbierta.Id, cajaOperacionId, TiposMovimientoCaja.Reintegro, -pendiente, ventaId, pago.PagoId, pago.MedioActual, null, motivo.Trim(), actor.Id, pago.MovimientoOriginalId, fecha),
                    cancellationToken);
            }
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
