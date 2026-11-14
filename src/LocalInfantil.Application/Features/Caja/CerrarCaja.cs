using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Caja;

public sealed class ArqueoCajaCambioException : InvalidOperationException
{
    public long EfectivoEsperadoActualMinor { get; }
    public long EfectivoContadoMinor { get; }

    public ArqueoCajaCambioException(long esperado, long contado)
        : base("La caja cambió durante el arqueo. Revisá nuevamente el resultado antes de confirmar el cierre.")
        => (EfectivoEsperadoActualMinor, EfectivoContadoMinor) = (esperado, contado);
}

public sealed class CerrarCaja
{
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;
    private readonly IRepositorioMercadoPago? _mercadoPago;

    public CerrarCaja(IRepositorioCaja caja, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IGeneradorIds ids, IReloj reloj, IRepositorioMercadoPago? mercadoPago = null)
        => (_caja, _autorizador, _fabrica, _auditoria, _ids, _reloj, _mercadoPago) = (caja, autorizador, fabrica, auditoria, ids, reloj, mercadoPago);

    /// <summary>Solo se solicita después de que la persona ingresó el efectivo contado.</summary>
    public async Task<ArqueoCaja> PrevisualizarAsync(long contadoMinor, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (contadoMinor < 0) throw new ArgumentOutOfRangeException(nameof(contadoMinor), "El efectivo contado no puede ser negativo.");
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosCaja.Cerrar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var abierta = await _caja.ObtenerAbiertaAsync(lectura, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta para cerrar.");
        var esperado = await _caja.CalcularEfectivoEsperadoAsync(lectura, abierta.Id, cancellationToken);
        return new(abierta.Id, contadoMinor, esperado, contadoMinor - esperado);
    }

    public async Task<ResultadoCierreCaja> EjecutarAsync(string sesionId, long contadoMinor, long esperadoMostradoMinor, string? observacion, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sesionId)) throw new ArgumentException("La sesión de caja es obligatoria.", nameof(sesionId));
        if (contadoMinor < 0) throw new ArgumentOutOfRangeException(nameof(contadoMinor), "El efectivo contado no puede ser negativo.");
        AbrirCaja.ExigirOperacion(operacionId);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCaja.Cerrar, cancellationToken);

        await using var tx = await _fabrica.IniciarAsync(cancellationToken);
        var abierta = await _caja.ObtenerAbiertaAsync(tx, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta para cerrar.");
        if (abierta.Id != sesionId) throw new InvalidOperationException("La caja activa cambió. Volvé a iniciar el cierre.");
        if (_mercadoPago is not null && (await _mercadoPago.ListarPendientesPorSesionAsync(tx, abierta.Id, cancellationToken)).Count > 0)
            throw new InvalidOperationException("Hay un cobro de Mercado Pago pendiente de resolver.");
        var esperado = await _caja.CalcularEfectivoEsperadoAsync(tx, abierta.Id, cancellationToken);
        var diferencia = contadoMinor - esperado;
        if (esperado != esperadoMostradoMinor) throw new ArqueoCajaCambioException(esperado, contadoMinor);
        var motivo = observacion?.Trim();
        if (diferencia != 0 && string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Indicá una observación para explicar la diferencia de caja.", nameof(observacion));
        if (await _caja.ExisteOperacionAsync(tx, operacionId, cancellationToken))
            throw new InvalidOperationException("Este cierre ya fue registrado.");

        var fecha = _reloj.UtcNow().ToString("o");
        await _caja.InsertarOperacionAsync(tx, new(operacionId, abierta.Id, TiposOperacionCaja.Cierre, actor.Id, fecha), cancellationToken);
        await _caja.CerrarSesionAsync(tx, abierta.Id, actor.Id, operacionId, fecha, esperado, contadoMinor, diferencia, motivo, cancellationToken);
        await _auditoria.RegistrarAsync(tx, new(operacionId, "caja.sesion_cerrada", ActorAuditoria.Usuario, actor.Id, "caja_sesion", abierta.Id, $"esperado_minor={esperado}; contado_minor={contadoMinor}; diferencia_minor={diferencia}; observacion={motivo ?? ""}"), cancellationToken);
        await tx.ConfirmarAsync(cancellationToken);
        return new(abierta.Id, operacionId, esperado, contadoMinor, diferencia, fecha);
    }
}
