using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioPagos
{
    Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string medio,
        long importeMinor,
        long? recibidoMinor,
        string creadoEn,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PagoRegistrado>> ListarPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
}
