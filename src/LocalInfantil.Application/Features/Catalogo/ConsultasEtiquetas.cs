using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Read-model de etiquetas: catálogo, precio actual y stock disponible en una sola lectura.</summary>
public sealed class ConsultasEtiquetas
{
    private readonly IRepositorioEtiquetas _repositorio;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasEtiquetas(IRepositorioEtiquetas repositorio, GestorSesiones sesiones, IFabricaSesionesPersistencia fabrica)
        => (_repositorio, _sesiones, _fabrica) = (repositorio, sesiones, fabrica);

    public async Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorProductoAsync(string productoId, string? idSesion, CancellationToken ct = default)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null) throw new SesionInvalidaException();
        await using var sesion = await _fabrica.IniciarLecturaAsync(ct);
        return await _repositorio.ListarPorProductoAsync(sesion, productoId, DateTime.UtcNow.ToString("o"), ct);
    }

    public async Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorIdsAsync(IReadOnlyList<string> varianteIds, string? idSesion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(varianteIds);
        if (idSesion is null || _sesiones.Obtener(idSesion) is null) throw new SesionInvalidaException();
        await using var sesion = await _fabrica.IniciarLecturaAsync(ct);
        return await _repositorio.ListarPorIdsAsync(sesion, varianteIds, DateTime.UtcNow.ToString("o"), ct);
    }
}
