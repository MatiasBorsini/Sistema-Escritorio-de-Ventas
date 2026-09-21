namespace LocalInfantil.Application.Contracts;

/// <summary>Sesión local iniciada: identidad autenticada para operar.</summary>
public sealed record SesionUsuario(string IdSesion, string UsuarioId, string Nombre, string Rol, string IniciadaEn);
