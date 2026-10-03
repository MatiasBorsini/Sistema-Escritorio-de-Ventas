using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

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
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public AnularVenta(
        IRepositorioVentas ventas,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _ventas = ventas;
        _movimientos = movimientos;
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

        var fecha = _reloj.UtcNow().ToString("o");
        await _ventas.InsertarReversionAsync(sesion, ventaId, operacionId, motivo.Trim(), actor.Id, fecha, cancellationToken);
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
