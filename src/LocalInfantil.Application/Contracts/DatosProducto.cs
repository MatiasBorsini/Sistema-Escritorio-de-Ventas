namespace LocalInfantil.Application.Contracts;

public sealed record NuevoProducto(string Nombre, string? Marca, string? Categoria);

public sealed record ProductoRegistrado(string Id, string Nombre, string? Marca, string? Categoria, bool Activo);
