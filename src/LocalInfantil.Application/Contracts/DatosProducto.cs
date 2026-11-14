namespace LocalInfantil.Application.Contracts;

public sealed record NuevoProducto(string Nombre, string? Marca, string? CategoriaId);

public sealed record ProductoRegistrado(string Id, string CodigoInterno, string Nombre, string? Marca, string? Categoria, string? CategoriaId, bool Activo);

/// <summary>
/// Fila de resumen para el master de Catálogo: cabecera del producto más
/// agregados derivados en la misma lectura (cantidad de variantes y stock
/// total como suma de existencias por variante). Sin precio ni SKU de
/// producto porque el dominio no los tiene a ese nivel.
/// </summary>
public sealed record ResumenProducto(
    string Id,
    string CodigoInterno,
    string Nombre,
    string? Marca,
    string? Categoria,
    string? CategoriaId,
    bool Activo,
    int CantidadVariantes,
    long StockTotal);
