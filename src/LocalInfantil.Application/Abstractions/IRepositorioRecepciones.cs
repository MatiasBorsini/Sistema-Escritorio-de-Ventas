using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioRecepciones
{
    Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string proveedorId,
        string operacionId,
        string? comprobanteRef,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task InsertarLineaAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string varianteId,
        long cantidad,
        long? costoUnitarioMinor,
        string? moneda,
        CancellationToken cancellationToken = default);

    Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);

    Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default);

    Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string operacionId,
        string motivo,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default);

    Task<RecepcionRegistrada?> ObtenerAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecepcionRegistrada>> ListarAsync(ISesionPersistencia sesion, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<bool> ExisteReversionPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
}
