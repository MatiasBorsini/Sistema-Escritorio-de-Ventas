using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Caja;

public sealed class ConsultasCaja
{
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasCaja(IRepositorioCaja caja, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica)
        => (_caja, _autorizador, _fabrica) = (caja, autorizador, fabrica);

    public async Task<DetalleSesionCaja?> ObtenerActualAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var sesion = await _caja.ObtenerAbiertaAsync(lectura, cancellationToken);
        return sesion is null ? null : await CrearDetalleAsync(lectura, sesion, true, cancellationToken);
    }

    public async Task<IReadOnlyList<SesionCajaRegistrada>> BuscarHistorialAsync(int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return await _caja.ListarSesionesAsync(lectura, limite, desplazamiento, cancellationToken);
    }

    public async Task<DetalleSesionCaja?> ObtenerDetalleAsync(string cajaSesionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cajaSesionId)) throw new ArgumentException("La sesión de caja es obligatoria.", nameof(cajaSesionId));
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var sesion = await _caja.ObtenerSesionAsync(lectura, cajaSesionId, cancellationToken);
        return sesion is null ? null : await CrearDetalleAsync(lectura, sesion, sesion.Estado == "abierta", cancellationToken);
    }

    public async Task<IReadOnlyList<PagoCajaActual>> BuscarPagosCorregiblesAsync(string filtroNumeroVenta, string? idSesion, CancellationToken cancellationToken = default)
    {
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return await _caja.BuscarPagosCorregiblesAsync(lectura, filtroNumeroVenta ?? string.Empty, 30, cancellationToken);
    }

    public async Task<IReadOnlyList<VentaCorreccionSugerida>> BuscarVentasParaCorreccionAsync(string filtro, string? idSesion, CancellationToken cancellationToken = default)
    {
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return await _caja.BuscarVentasParaCorreccionAsync(lectura, filtro ?? string.Empty, 15, cancellationToken);
    }

    public async Task<IReadOnlyList<PagoCajaActual>> ListarPagosCorregiblesPorVentaAsync(string ventaId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ventaId)) throw new ArgumentException("La venta es obligatoria.", nameof(ventaId));
        await _autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosCaja.Consultar, cancellationToken);
        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var pagos = await _caja.ListarPagosActualesPorVentaAsync(lectura, ventaId, cancellationToken);
        return pagos.Where(p => p.EstadoVenta == "confirmada"
            && p.MedioOriginal != MediosPago.MercadoPago
            && p.MedioActual != MediosPago.MercadoPago
            && !p.Corregido
            && p.MovimientoOriginalId is not null).ToArray();
    }

    private async Task<DetalleSesionCaja> CrearDetalleAsync(ISesionPersistencia lectura, SesionCajaRegistrada sesion, bool calcularEnVivo, CancellationToken cancellationToken)
    {
        var esperado = calcularEnVivo
            ? await _caja.CalcularEfectivoEsperadoAsync(lectura, sesion.Id, cancellationToken)
            : sesion.EfectivoEsperadoMinor;
        var medios = await _caja.ResumirMediosAsync(lectura, sesion.Id, cancellationToken);
        var resumen = await _caja.ResumirMovimientosAsync(lectura, sesion.Id, cancellationToken);
        var movimientos = await _caja.ListarMovimientosAsync(lectura, sesion.Id, cancellationToken);
        return new(sesion, esperado, medios, resumen, movimientos);
    }

    private static string ExigirSesion(string? idSesion) => idSesion ?? throw new SesionInvalidaException();
}
