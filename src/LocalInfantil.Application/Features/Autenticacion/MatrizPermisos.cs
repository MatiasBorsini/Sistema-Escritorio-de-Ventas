using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Application.Features.Caja;

namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Matriz rol→permiso configurable. Niega por defecto todo lo no concedido
/// explícitamente. Solo trae precargados los permisos comerciales confirmados;
/// la matriz completa pertenece a D-03.
/// Ventas: confirmar habilitado para Dueña y Empleada; anular solo Dueña.
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
        matriz.Permitir(RolesLocal.Duena, PermisosVentas.VentasConfirmar);
        matriz.Permitir(RolesLocal.Empleada, PermisosVentas.VentasConfirmar);
        matriz.Permitir(RolesLocal.Duena, PermisosVentas.VentasAnular);
        foreach (var permiso in PermisosCaja.Todos)
            matriz.Permitir(RolesLocal.Duena, permiso);
        foreach (var permiso in new[] { PermisosCaja.Abrir, PermisosCaja.Consultar, PermisosCaja.Movimiento, PermisosCaja.Cerrar })
            matriz.Permitir(RolesLocal.Empleada, permiso);
        matriz.Denegar(RolesLocal.Empleada, PermisosCaja.RetiroPropietario);
        matriz.Denegar(RolesLocal.Empleada, PermisosCaja.CorregirMedio);
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
