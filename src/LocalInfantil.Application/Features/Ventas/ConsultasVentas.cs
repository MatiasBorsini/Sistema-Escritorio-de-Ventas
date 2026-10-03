using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Consultas de ventas. Requieren sesión vigente pero ningún permiso
/// específico: el historial es de lectura y conserva ventas anuladas con
/// sus snapshots originales.
/// </summary>
public sealed class ConsultasVentas
{
    private readonly IRepositorioVentas _ventas;
    private readonly IRepositorioPagos _pagos;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasVentas(
        IRepositorioVentas ventas,
        IRepositorioPagos pagos,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica)
    {
        _ventas = ventas;
        _pagos = pagos;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    /// <summary>
    /// Resumen por página en una sola lectura (hasta limite+1 filas para el
    /// peek de HaySiguiente). Estado: "todas", "confirmadas" o "anuladas".
    /// </summary>
    public async Task<IReadOnlyList<ResumenVenta>> BuscarResumenAsync(string texto, string estado, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _ventas.BuscarResumenAsync(sesion, texto ?? string.Empty, estado, limite, desplazamiento, cancellationToken);
    }

    public async Task<FichaVenta?> ObtenerFichaAsync(string ventaId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var venta = await _ventas.ObtenerPorIdAsync(sesion, ventaId, cancellationToken);
        if (venta is null)
        {
            return null;
        }

        var lineas = await _ventas.ListarLineasAsync(sesion, ventaId, cancellationToken);
        var pagos = await _pagos.ListarPorVentaAsync(sesion, ventaId, cancellationToken);
        var anulacion = await _ventas.ObtenerReversionAsync(sesion, ventaId, cancellationToken);
        return new FichaVenta(venta, lineas, pagos, anulacion);
    }

    public async Task<VentaRegistrada?> BuscarPorNumeroAsync(string numero, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _ventas.ObtenerPorNumeroAsync(sesion, numero, cancellationToken);
    }

    public async Task<VentaRegistrada?> BuscarPorOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _ventas.ObtenerPorOperacionAsync(sesion, operacionId, cancellationToken);
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
