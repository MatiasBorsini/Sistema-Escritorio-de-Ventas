namespace LocalInfantil.Application.Contracts;

public sealed record NuevoUsuario(string Nombre, string Rol);

public sealed record UsuarioRegistrado(string Id, string Nombre, string Rol, bool Activo);
