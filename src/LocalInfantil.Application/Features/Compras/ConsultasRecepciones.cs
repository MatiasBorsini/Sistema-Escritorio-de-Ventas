using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Compras;

public sealed class ConsultasRecepciones
{
    private readonly IRepositorioRecepciones _recepciones;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasRecepciones(IRepositorioRecepciones recepciones, GestorSesiones sesiones, IFabricaSesionesPersistencia fabrica)
    {
        _recepciones = recepciones;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<RecepcionRegistrada?> ObtenerAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ObtenerAsync(sesion, id, cancellationToken);
    }

    public async Task<IReadOnlyList<RecepcionRegistrada>> ListarAsync(int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ListarAsync(sesion, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(string recepcionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ListarLineasAsync(sesion, recepcionId, cancellationToken);
    }

    public async Task<bool> EstaRevertidaAsync(string recepcionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExisteReversionAsync(sesion, recepcionId, cancellationToken);
    }

    public async Task<bool> ExisteOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExistePorOperacionAsync(sesion, operacionId, cancellationToken);
    }

    public async Task<bool> ExisteReversionPorOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExisteReversionPorOperacionAsync(sesion, operacionId, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null)
            throw new SesionInvalidaException();
        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
