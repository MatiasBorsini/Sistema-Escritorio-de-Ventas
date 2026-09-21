namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Permiso técnico de gestión de cuentas. Distinto de los permisos
/// comerciales: no concede ventas, reintegros, caja ni otras operaciones.
/// </summary>
public static class PermisosAdministracion
{
    /// <summary>Crear y desactivar cuentas, cambiar y restablecer contraseñas. Solo Administrador.</summary>
    public const string UsuariosAdministrar = "usuarios.administrar";
}
