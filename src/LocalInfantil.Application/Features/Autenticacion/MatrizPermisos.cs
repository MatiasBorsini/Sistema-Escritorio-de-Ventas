namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Matriz rol→permiso configurable. Niega por defecto todo lo no concedido
/// explícitamente. Solo trae precargados los permisos comerciales confirmados;
/// la matriz completa pertenece a D-03.
/// </summary>
public sealed class MatrizPermisos
{
    private readonly Dictionary<(string Rol, string Permiso), bool> _reglas = new();

    public static MatrizPermisos Vacia() => new();

    public static MatrizPermisos ConValoresConfirmados()
    {
        var matriz = new MatrizPermisos();
        matriz.Permitir(RolesLocal.Duena, PermisosComerciales.ReintegroAutorizar);
        matriz.Permitir(RolesLocal.Empleada, PermisosComerciales.ReintegroAutorizar);
        matriz.Permitir(RolesLocal.Duena, PermisosComerciales.ReintegroMedioExcepcional);
        matriz.Permitir(RolesLocal.Duena, PermisosAdministracion.UsuariosAdministrar);
        return matriz;
    }

    public void Permitir(string rol, string permiso)
    {
        _reglas[(rol, permiso)] = true;
    }

    public void Denegar(string rol, string permiso)
    {
        _reglas[(rol, permiso)] = false;
    }

    public bool Permite(string rol, string permiso)
    {
        return _reglas.TryGetValue((rol, permiso), out var concedido) && concedido;
    }
}
