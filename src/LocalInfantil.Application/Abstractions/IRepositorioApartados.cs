using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Libro de clasificación de stock no vendible. Derivados por variante:
/// apartado = SUM(+apartar, −reincorporar, −baja_definitiva);
/// disponible = físico − apartado. Ningún método borra ni edita historial.
/// </summary>
public interface IRepositorioApartados
{
    Task<string> RegistrarAsync(
        ISesionPersistencia sesion,
        string varianteId,
        string tipo,
        long cantidad,
        string motivo,
        string operacionId,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task<long> SumarApartadoPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default);

    Task<long> ContarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApartadoHistorial>> ListarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task GuardarFotoConteoAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long apartadoEsperado, long clasificacionesContadas, CancellationToken cancellationToken = default);

    Task<FotoClasificacion?> ObtenerFotoConteoAsync(ISesionPersistencia sesion, string conteoId, string varianteId, CancellationToken cancellationToken = default);
}
