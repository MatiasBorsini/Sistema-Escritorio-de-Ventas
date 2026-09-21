using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Respaldo local consistente de SQLite. El llamador indica origen y destino:
/// la ruta definitiva de producción sigue pendiente (D-09). Los respaldos
/// generados quedan fuera del repositorio y del instalador.
/// </summary>
public interface IServicioRespaldos
{
    Task<RespaldoVerificado> CrearRespaldoAsync(
        string rutaOrigen,
        string directorioDestino,
        IReadOnlyList<int>? versionesEsperadas,
        string prefijo = "respaldo",
        CancellationToken cancellationToken = default);

    Task<VerificacionRespaldo> VerificarAsync(
        string rutaRespaldo,
        IReadOnlyList<int>? versionesEsperadas,
        CancellationToken cancellationToken = default);

    Task<string> RestaurarEnsayoAsync(
        string rutaRespaldo,
        string directorioTemporal,
        IReadOnlyList<int>? versionesEsperadas,
        CancellationToken cancellationToken = default);
}
