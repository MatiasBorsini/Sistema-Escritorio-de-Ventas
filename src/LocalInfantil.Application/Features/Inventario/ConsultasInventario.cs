using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Consultas de existencias e historial. Requieren sesión vigente pero ningún
/// permiso específico (interino hasta D-03). Las existencias se derivan de
/// movimientos confirmados; el historial conserva variantes inactivas.
/// Físico = disponible + apartado; el disponible es vendible (puerta de venta
/// futura F04) y el apartado es no vendible (dañado/en revisión).
/// El identificador operativo es el código interno (ex-SKU).
/// </summary>
public sealed class ConsultasInventario
{
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioConteos _conteos;
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioProductos _productos;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasInventario(
        IRepositorioMovimientos movimientos,
        IRepositorioVariantes variantes,
        IRepositorioConteos conteos,
        IRepositorioApartados apartados,
        IRepositorioProductos productos,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica)
    {
        _movimientos = movimientos;
        _variantes = variantes;
        _conteos = conteos;
        _apartados = apartados;
        _productos = productos;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<ExistenciaVariante?> ObtenerExistenciaAsync(string codigo, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorCodigoInternoAsync(sesion, codigo.Trim(), cancellationToken);
        if (variante is null)
        {
            return null;
        }

        var existencias = await _movimientos.SumarPorVarianteAsync(sesion, variante.Id, cancellationToken);
        return new ExistenciaVariante(variante.Id, variante.CodigoInterno, existencias);
    }

    public async Task<IReadOnlyList<MovimientoHistorial>> HistorialPorSkuAsync(string codigo, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorCodigoInternoAsync(sesion, codigo.Trim(), cancellationToken);
        if (variante is null)
        {
            return Array.Empty<MovimientoHistorial>();
        }

        return await _movimientos.ListarPorVarianteAsync(sesion, variante.Id, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<InformeConteoLinea>> InformeConteoAsync(string conteoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.InformeAsync(sesion, conteoId, cancellationToken);
    }

    public async Task<ExistenciaClasificada?> ObtenerClasificadaAsync(string codigo, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorCodigoInternoAsync(sesion, codigo.Trim(), cancellationToken);
        if (variante is null)
        {
            return null;
        }

        var fisico = await _movimientos.SumarPorVarianteAsync(sesion, variante.Id, cancellationToken);
        var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, variante.Id, cancellationToken);
        var producto = await _productos.ObtenerPorIdAsync(sesion, variante.ProductoId, cancellationToken);
        return new ExistenciaClasificada(variante.Id, variante.CodigoInterno, fisico, fisico - apartado, apartado,
            producto?.Nombre ?? string.Empty, variante.Talle, variante.Color);
    }

    public async Task<IReadOnlyList<ApartadoHistorial>> HistorialApartadosPorSkuAsync(string codigo, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorCodigoInternoAsync(sesion, codigo.Trim(), cancellationToken);
        if (variante is null)
        {
            return Array.Empty<ApartadoHistorial>();
        }

        return await _apartados.ListarPorVarianteAsync(sesion, variante.Id, limite, desplazamiento, cancellationToken);
    }

    /// <summary>
    /// Puerta de venta futura (F04): el POS debe validar contra Disponible,
    /// nunca contra el físico total. Sin implementar ventas en este bloque.
    /// </summary>
    public async Task<StockVendible?> ObtenerStockVendibleAsync(string varianteId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            return null;
        }

        var fisico = await _movimientos.SumarPorVarianteAsync(sesion, varianteId, cancellationToken);
        var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, varianteId, cancellationToken);
        return new StockVendible(varianteId, variante.CodigoInterno, fisico - apartado);
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
