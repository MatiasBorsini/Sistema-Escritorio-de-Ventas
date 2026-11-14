using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Lectura para la pantalla de conciliación: importaciones recientes y
/// liquidaciones pendientes. Solo lectura; no aplica ni vincula nada.
/// Requiere el permiso de conciliar.
/// </summary>
public sealed class ConsultarConciliacionMp
{
    private readonly IRepositorioLiquidacionesMp _liquidaciones;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultarConciliacionMp(
        IRepositorioLiquidacionesMp liquidaciones,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica)
    {
        _liquidaciones = liquidaciones;
        _autorizador = autorizador;
        _fabrica = fabrica;
    }

    public async Task<IReadOnlyList<LiquidacionMpRegistrada>> ListarPendientesAsync(
        string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return await _liquidaciones.ListarPendientesAsync(lectura, cancellationToken);
    }

    public async Task<IReadOnlyList<ImportacionMpRegistrada>> ListarImportacionesAsync(
        int limite, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return await _liquidaciones.ListarImportacionesAsync(lectura, limite, cancellationToken);
    }
}
