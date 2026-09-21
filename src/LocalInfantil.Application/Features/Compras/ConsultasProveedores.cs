using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>Consultas de proveedores. Requieren sesión vigente, sin permiso específico (interino hasta D-03).</summary>
public sealed class ConsultasProveedores
{
    private readonly IRepositorioProveedores _proveedores;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasProveedores(
        IRepositorioProveedores proveedores,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica)
    {
        _proveedores = proveedores;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<ProveedorRegistrado?> ObtenerAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _proveedores.ObtenerPorIdAsync(sesion, id, cancellationToken);
    }

    public async Task<IReadOnlyList<ProveedorRegistrado>> ListarAsync(string? idSesion, bool soloActivos, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _proveedores.ListarAsync(sesion, soloActivos, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null)
        {
            throw new SesionInvalidaException();
        }

        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
