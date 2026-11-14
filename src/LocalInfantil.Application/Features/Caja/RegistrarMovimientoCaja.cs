using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Caja;

public sealed class RegistrarMovimientoCaja
{
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public RegistrarMovimientoCaja(IRepositorioCaja caja, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IReloj reloj)
        => (_caja, _autorizador, _fabrica, _auditoria, _reloj) = (caja, autorizador, fabrica, auditoria, reloj);

    public async Task EjecutarAsync(string categoria, long importeMinor, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (importeMinor <= 0) throw new ArgumentOutOfRangeException(nameof(importeMinor), "El importe debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(motivo)) throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        AbrirCaja.ExigirOperacion(operacionId);
        if (idSesion is null) throw new SesionInvalidaException();
        var ingreso = CategoriasMovimientoCaja.Ingresos.Contains(categoria, StringComparer.Ordinal);
        var egreso = CategoriasMovimientoCaja.Egresos.Contains(categoria, StringComparer.Ordinal);
        if (!ingreso && !egreso) throw new ArgumentException("La categoría de movimiento no es válida.", nameof(categoria));

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCaja.Movimiento, cancellationToken);
        if (categoria == CategoriasMovimientoCaja.RetiroPropietario)
            actor = await _autorizador.AutorizarAsync(idSesion, PermisosCaja.RetiroPropietario, cancellationToken);

        await using var tx = await _fabrica.IniciarAsync(cancellationToken);
        var caja = await _caja.ObtenerAbiertaAsync(tx, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta para registrar el movimiento.");
        if (await _caja.ExisteOperacionAsync(tx, operacionId, cancellationToken))
            throw new InvalidOperationException("Esta operación de caja ya fue registrada.");

        var fecha = _reloj.UtcNow().ToString("o");
        var cajaOperacion = await _caja.InsertarOperacionAsync(tx, new(operacionId, caja.Id, TiposOperacionCaja.MovimientoManual, actor.Id, fecha), cancellationToken);
        var tipo = ingreso ? TiposMovimientoCaja.Ingreso : TiposMovimientoCaja.Egreso;
        var efecto = ingreso ? importeMinor : -importeMinor;
        await _caja.InsertarMovimientoAsync(tx, new(caja.Id, cajaOperacion, tipo, efecto, null, null, MediosCaja.Efectivo, categoria, motivo.Trim(), actor.Id, null, fecha), cancellationToken);
        await _auditoria.RegistrarAsync(tx, new(operacionId, "caja.movimiento_manual", ActorAuditoria.Usuario, actor.Id, "caja_sesion", caja.Id, $"tipo={tipo}; categoria={categoria}; importe_minor={efecto}; motivo={motivo.Trim()}"), cancellationToken);
        await tx.ConfirmarAsync(cancellationToken);
    }

}
