using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Reportes;

/// <summary>
/// Lectura agregada de reportes. Cada área exige su permiso efectivo
/// (backend real, no solo pestaña oculta): reportes.ventas, reportes.caja,
/// reportes.inventario. Sin área de costos hasta D-07.
/// </summary>
public sealed class ConsultasReportes
{
    private readonly IRepositorioReportes _reportes;
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasReportes(
        IRepositorioReportes reportes,
        IRepositorioCaja caja,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica)
    {
        _reportes = reportes;
        _caja = caja;
        _autorizador = autorizador;
        _fabrica = fabrica;
    }

    public async Task<ResumenVentasPeriodo> ResumenVentasAsync(string desdeUtc, string hastaUtc, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Ventas, cancellationToken);
        return await _reportes.ResumenVentasAsync(sesion, desdeUtc, hastaUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<FilaDiaria>> VentasPorDiaAsync(string desdeUtc, string hastaUtc, string desplazamientoHoras, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Ventas, cancellationToken);
        return await _reportes.VentasPorDiaAsync(sesion, desdeUtc, hastaUtc, desplazamientoHoras, cancellationToken);
    }

    public async Task<IReadOnlyList<DetalleVentaReporte>> DetalleVentasAsync(string desdeUtc, string hastaUtc, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Ventas, cancellationToken);
        return await _reportes.DetalleVentasAsync(sesion, desdeUtc, hastaUtc, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<DesgloseMedio>> DesgloseMediosAsync(string desdeUtc, string hastaUtc, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Ventas, cancellationToken);
        return await _reportes.DesgloseMediosAsync(sesion, desdeUtc, hastaUtc, cancellationToken);
    }

    public async Task<IReadOnlyList<SesionCajaRegistrada>> HistorialCajaAsync(string desdeUtc, string hastaUtc, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Caja, cancellationToken);
        return await _caja.ListarSesionesPorFechaAsync(sesion, desdeUtc, hastaUtc, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<FilaStockActual>> StockActualAsync(string texto, string? categoriaId, bool soloCero, long? hastaUnidades, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Inventario, cancellationToken);
        return await _reportes.StockActualAsync(sesion, texto ?? string.Empty, categoriaId, soloCero, hastaUnidades, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<RankingVenta>> RankingVentasAsync(string desdeUtc, string hastaUtc, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Inventario, cancellationToken);
        return await _reportes.RankingVentasAsync(sesion, desdeUtc, hastaUtc, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<RankingVenta>> RankingPorProductoAsync(string desdeUtc, string hastaUtc, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Inventario, cancellationToken);
        return await _reportes.RankingPorProductoAsync(sesion, desdeUtc, hastaUtc, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<SinVentas>> SinVentasAsync(string corteUtc, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionAutorizadaAsync(idSesion, PermisosReportes.Inventario, cancellationToken);
        return await _reportes.SinVentasAsync(sesion, corteUtc, limite, desplazamiento, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionAutorizadaAsync(string? idSesion, string permiso, CancellationToken cancellationToken)
    {
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), permiso, cancellationToken);
        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }

    private static string ExigirSesion(string? idSesion) => idSesion ?? throw new SesionInvalidaException();
}
