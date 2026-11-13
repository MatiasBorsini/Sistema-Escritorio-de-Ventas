using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Cambios;

public sealed class ConsultasCambios(IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioCambios cambios, IRepositorioVentas ventas,
    IRepositorioVariantes variantes, IRepositorioProductos productos, IRepositorioPrecios precios, IRepositorioMovimientos movimientos, IRepositorioApartados apartados, IReloj reloj)
{
    public async Task<IReadOnlyList<ResumenVenta>> BuscarVentasAsync(string filtro, string? desde, string? hasta, string? idSesion, CancellationToken ct=default)
    {
        if(idSesion is null) throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        return await cambios.BuscarVentasAsync(lectura,filtro,desde,hasta,50,ct);
    }

    public async Task<IReadOnlyList<LineaCambioDisponible>> ObtenerEntradasDisponiblesAsync(string ventaId,string? idSesion,CancellationToken ct=default)
    {
        if(idSesion is null) throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        var lineas=await ventas.ListarLineasAsync(lectura,ventaId,ct); var result=new List<LineaCambioDisponible>(lineas.Count);
        foreach(var linea in lineas) { var disponible=await cambios.CantidadDisponibleAsync(lectura,linea.Id,ct); result.Add(new(linea,disponible)); }
        return result;
    }

    public async Task<IReadOnlyList<ResumenCambio>> ListarAsync(string ventaId,string? idSesion,CancellationToken ct=default)
    {
        if(idSesion is null) throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        return await cambios.ListarPorVentaAsync(lectura,ventaId,ct);
    }

    public async Task<OpcionVarianteCambio?> ObtenerVarianteExactaAsync(string codigoInterno,string? idSesion,CancellationToken ct=default)
    {
        if(idSesion is null) throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        var variante=await variantes.ObtenerPorCodigoInternoAsync(lectura,codigoInterno.Trim(),ct);
        if(variante is null || !variante.Activa) return null;
        var fecha=reloj.UtcNow().ToString("o"); var precio=await precios.ObtenerVigenteAsync(lectura,variante.Id,fecha,ct);
        if(precio is null) return null;
        var producto=await productos.ObtenerPorIdAsync(lectura,variante.ProductoId,ct);
        var disponible=await movimientos.SumarPorVarianteAsync(lectura,variante.Id,ct)-await apartados.SumarApartadoPorVarianteAsync(lectura,variante.Id,ct);
        return new(variante.Id,variante.CodigoInterno,producto?.Nombre??variante.CodigoInterno,variante.Talle,variante.Color,precio.ImporteMinor,Math.Max(0,disponible));
    }

    /// <summary>
    /// Sugerencias de artículos nuevos para un cambio: solo variantes activas,
    /// con precio vigente y stock disponible resueltos en la misma lectura.
    /// Solo lectura, sin reglas comerciales nuevas.
    /// </summary>
    public async Task<IReadOnlyList<SugerenciaVariante>> BuscarArticulosNuevosAsync(string texto,int limite,string? idSesion,CancellationToken ct=default)
    {
        if(idSesion is null) throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        return await variantes.BuscarSugerenciasConPrecioStockAsync(lectura,texto,limite,reloj.UtcNow().ToString("o"),ct);
    }
}
