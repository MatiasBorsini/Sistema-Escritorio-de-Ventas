namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>Roles locales conocidos. La matriz completa de permisos sigue pendiente (D-03).</summary>
public static class RolesLocal
{
    /// <summary>Rol retirado: solo existió antes de la migración V6, que lo convierte en Dueña.</summary>
    public const string Administrador = "administrador";

    public const string Duena = "duena";
    public const string Empleada = "empleada";
}
