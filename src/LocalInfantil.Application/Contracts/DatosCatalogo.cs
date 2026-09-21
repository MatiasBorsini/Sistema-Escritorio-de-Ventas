namespace LocalInfantil.Application.Contracts;

/// <summary>Edición con reemplazo total: nombre obligatorio, marca/categoría se reemplazan (null las vacía).</summary>
public sealed record EdicionProducto(string Nombre, string? Marca, string? Categoria);

/// <summary>Edición con reemplazo total de talle/color (null los vacía). SKU y producto inmutables.</summary>
public sealed record EdicionVariante(string? Talle, string? Color);
