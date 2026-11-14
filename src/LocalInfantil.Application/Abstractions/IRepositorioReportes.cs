using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Solo lectura agregada para reportes. Sin tablas resumen: todo deriva de
/// registros transaccionales con GROUP BY/SUM/COUNT en una lectura por
/// consulta. Rangos semiabiertos [desdeUtc, hastaUtc) en ISO-8601.
/// </summary>
public interface IRepositorioReportes
{
    Task<ResumenVentasPeriodo> ResumenVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FilaDiaria>> VentasPorDiaAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, string desplazamientoHoras, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DetalleVentaReporte>> DetalleVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DesgloseMedio>> DesgloseMediosAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FilaStockActual>> StockActualAsync(ISesionPersistencia sesion, string texto, string? categoriaId, bool soloCero, long? hastaUnidades, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RankingVenta>> RankingVentasAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RankingVenta>> RankingPorProductoAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SinVentas>> SinVentasAsync(ISesionPersistencia sesion, string corteUtc, int limite, int desplazamiento, CancellationToken cancellationToken = default);
}
