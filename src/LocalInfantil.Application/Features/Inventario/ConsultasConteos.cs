using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Inventario;

public sealed class ConsultasConteos
{
    private readonly IRepositorioConteos _conteos;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasConteos(IRepositorioConteos conteos, GestorSesiones sesiones, IFabricaSesionesPersistencia fabrica)
    {
        _conteos = conteos;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<ConteoRegistrado?> ObtenerAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var s = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.ObtenerAsync(s, id, cancellationToken);
    }

    public async Task<IReadOnlyList<ConteoRegistrado>> ListarAsync(int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var s = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.ListarAsync(s, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<DetalleConteo>> ListarDetallesAsync(string conteoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var s = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.ListarDetallesAsync(s, conteoId, cancellationToken);
    }

    public async Task<IReadOnlyList<InformeConteoLinea>> InformeAsync(string conteoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var s = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.InformeAsync(s, conteoId, cancellationToken);
    }

    public async Task<IReadOnlyList<LineaCorreccionConteo>> ListarLineasCorreccionAsync(string conteoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var s = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.ListarLineasCorreccionAsync(s, conteoId, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null) throw new SesionInvalidaException();
        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
