namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Permiso técnico de gestión de cuentas. Distinto de los permisos
/// comerciales: no concede ventas, reintegros, caja ni otras operaciones.
/// Hoy lo posee el rol Dueña; no es configurable por usuario.
/// </summary>
public static class PermisosAdministracion
{
    /// <summary>Crear y desactivar cuentas, cambiar y restablecer contraseñas. Solo Dueña.</summary>
    public const string UsuariosAdministrar = "usuarios.administrar";
}
