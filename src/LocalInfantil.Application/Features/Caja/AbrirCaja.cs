using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Caja;

public sealed class AbrirCaja
{
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public AbrirCaja(IRepositorioCaja caja, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IReloj reloj)
        => (_caja, _autorizador, _fabrica, _auditoria, _reloj) = (caja, autorizador, fabrica, auditoria, reloj);

    public async Task<string> EjecutarAsync(long fondoInicialMinor, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (fondoInicialMinor < 0) throw new ArgumentOutOfRangeException(nameof(fondoInicialMinor), "El fondo inicial no puede ser negativo.");
        ExigirOperacion(operacionId);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCaja.Abrir, cancellationToken);

        await using var tx = await _fabrica.IniciarAsync(cancellationToken);
        if (await _caja.ExisteOperacionAsync(tx, operacionId, cancellationToken))
            throw new InvalidOperationException("Esta apertura ya fue registrada.");
        if (await _caja.ObtenerAbiertaAsync(tx, cancellationToken) is not null)
            throw new InvalidOperationException("Ya hay una caja abierta.");

        var fecha = _reloj.UtcNow().ToString("o");
        var idCaja = await _caja.AbrirSesionAsync(tx, actor.Id, fondoInicialMinor, operacionId, fecha, cancellationToken);
        await _caja.InsertarOperacionAsync(tx, new(operacionId, idCaja, TiposOperacionCaja.Apertura, actor.Id, fecha), cancellationToken);
        await _auditoria.RegistrarAsync(tx, new(operacionId, "caja.sesion_abierta", ActorAuditoria.Usuario, actor.Id, "caja_sesion", idCaja, $"fondo_inicial_minor={fondoInicialMinor}; moneda=ARS"), cancellationToken);
        await tx.ConfirmarAsync(cancellationToken);
        return idCaja;
    }

    internal static void ExigirOperacion(string operacionId)
    {
        if (string.IsNullOrWhiteSpace(operacionId)) throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
    }
}
