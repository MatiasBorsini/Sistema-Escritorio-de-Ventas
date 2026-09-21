namespace LocalInfantil.Application.Contracts;

/// <summary>Edición con reemplazo total: nombre obligatorio, marca/categoría se reemplazan (null las vacía).</summary>
public sealed record EdicionProducto(string Nombre, string? Marca, string? Categoria);

/// <summary>Edición con reemplazo total de talle/color (null los vacía). SKU y producto inmutables.</summary>
public sealed record EdicionVariante(string? Talle, string? Color);

/// <summary>Variante con su precio vigente e existencias derivadas, en una sola lectura.</summary>
public sealed record FichaVariante(
    string Id,
    string Sku,
    string? Talle,
    string? Color,
    bool Activa,
    long? PrecioMinor,
    string? Moneda,
    long Existencias);

/// <summary>Ficha de producto: cabecera más sus variantes con precio y existencias.</summary>
public sealed record FichaProducto(ProductoRegistrado Producto, IReadOnlyList<FichaVariante> Variantes);

/// <summary>Una fila de carga inicial: cantidad a establecer para una variante.</summary>
public sealed record FilaCargaInicial(string VarianteId, long Cantidad);
