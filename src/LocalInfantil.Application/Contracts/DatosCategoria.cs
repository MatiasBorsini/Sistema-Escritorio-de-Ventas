namespace LocalInfantil.Application.Contracts;

public sealed record CategoriaRegistrada(string Id, string Nombre, string Prefijo, int Orden, bool Activa);

public sealed record NuevaCategoria(string Nombre, string Prefijo, int Orden);