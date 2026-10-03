using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Caja;

public sealed class CorregirMedioPago
{
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public CorregirMedioPago(IRepositorioCaja caja, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IReloj reloj)
        => (_caja, _autorizador, _fabrica, _auditoria, _reloj) = (caja, autorizador, fabrica, auditoria, reloj);

    public async Task EjecutarAsync(string pagoId, string medioNuevo, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pagoId)) throw new ArgumentException("El pago es obligatorio.", nameof(pagoId));
        if (!MediosPago.Todos.Contains(medioNuevo, StringComparer.Ordinal)) throw new ArgumentException("El medio nuevo no es válido.", nameof(medioNuevo));
        if (string.IsNullOrWhiteSpace(motivo)) throw new ArgumentException("El motivo de la corrección es obligatorio.", nameof(motivo));
        AbrirCaja.ExigirOperacion(operacionId);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCaja.CorregirMedio, cancellationToken);

        await using var tx = await _fabrica.IniciarAsync(cancellationToken);
        var caja = await _caja.ObtenerAbiertaAsync(tx, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta para registrar la corrección.");
        if (await _caja.ExisteOperacionAsync(tx, operacionId, cancellationToken)) throw new InvalidOperationException("Esta corrección ya fue registrada.");
        var pago = await _caja.ObtenerPagoActualAsync(tx, pagoId, cancellationToken)
            ?? throw new InvalidOperationException("El pago original no existe.");
        if (pago.EstadoVenta != "confirmada") throw new InvalidOperationException("No se puede corregir el medio de una venta anulada.");
        if (pago.Corregido) throw new InvalidOperationException("Este pago ya tiene una corrección. No se admiten cadenas de correcciones.");
        if (pago.MedioActual == medioNuevo) throw new InvalidOperationException("El pago ya está registrado con ese medio.");
        if (pago.MovimientoOriginalId is null)
            throw new InvalidOperationException("El pago es anterior al control de caja y no tiene un movimiento de origen que corregir.");

        var fecha = _reloj.UtcNow().ToString("o");
        var cajaOperacionId = await _caja.InsertarOperacionAsync(tx, new(operacionId, caja.Id, TiposOperacionCaja.CorreccionMedio, actor.Id, fecha), cancellationToken);
        await _caja.InsertarCorreccionPagoAsync(tx, pagoId, pago.MedioActual, medioNuevo, pago.ImporteMinor, motivo.Trim(), actor.Id, operacionId, cajaOperacionId, fecha, cancellationToken);
        await _caja.InsertarMovimientoAsync(tx, new(caja.Id, cajaOperacionId, TiposMovimientoCaja.Correccion, -pago.ImporteMinor, pago.VentaId, pagoId, pago.MedioActual, null, motivo.Trim(), actor.Id, pago.MovimientoOriginalId, fecha), cancellationToken);
        await _caja.InsertarMovimientoAsync(tx, new(caja.Id, cajaOperacionId, TiposMovimientoCaja.Correccion, pago.ImporteMinor, pago.VentaId, pagoId, medioNuevo, null, motivo.Trim(), actor.Id, pago.MovimientoOriginalId, fecha), cancellationToken);
        await _auditoria.RegistrarAsync(tx, new(operacionId, "caja.medio_pago_corregido", ActorAuditoria.Usuario, actor.Id, "pago", pagoId, $"medio_anterior={pago.MedioActual}; medio_nuevo={medioNuevo}; importe_minor={pago.ImporteMinor}; motivo={motivo.Trim()}"), cancellationToken);
        await tx.ConfirmarAsync(cancellationToken);
    }
}
