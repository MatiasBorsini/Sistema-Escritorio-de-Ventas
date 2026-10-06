using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Respaldos;

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
        foreach (var permiso in PermisosRespaldos.Todos) matriz.Permitir(RolesLocal.Duena, permiso);
        matriz.Permitir(RolesLocal.Empleada, PermisosRespaldos.Consultar);
        matriz.Permitir(RolesLocal.Duena, PermisosClientes.Consultar);
        matriz.Permitir(RolesLocal.Empleada, PermisosClientes.Consultar);
        matriz.Permitir(RolesLocal.Duena, PermisosClientes.Administrar);
        matriz.Denegar(RolesLocal.Empleada, PermisosClientes.Administrar);
        matriz.Permitir(RolesLocal.Duena, PermisosCuentaCorriente.Consultar);
        matriz.Permitir(RolesLocal.Empleada, PermisosCuentaCorriente.Consultar);
        matriz.Permitir(RolesLocal.Duena, PermisosCuentaCorriente.VenderFiado);
        matriz.Denegar(RolesLocal.Empleada, PermisosCuentaCorriente.VenderFiado);
        matriz.Permitir(RolesLocal.Duena, PermisosCuentaCorriente.Abonar);
        matriz.Permitir(RolesLocal.Empleada, PermisosCuentaCorriente.Abonar);
        matriz.Permitir(RolesLocal.Duena, PermisosCuentaCorriente.Ajustar);
        matriz.Denegar(RolesLocal.Empleada, PermisosCuentaCorriente.Ajustar);
        matriz.Permitir(RolesLocal.Duena, PermisosCuentaCorriente.ConfigurarLimite);
        matriz.Denegar(RolesLocal.Empleada, PermisosCuentaCorriente.ConfigurarLimite);
        matriz.Permitir(RolesLocal.Duena, PermisosVentas.VentasConfirmar);
        matriz.Permitir(RolesLocal.Empleada, PermisosVentas.VentasConfirmar);
        matriz.Permitir(RolesLocal.Duena, PermisosVentas.VentasAnular);
        foreach (var permiso in PermisosCambios.Todos)
        {
            matriz.Permitir(RolesLocal.Duena, permiso);
            matriz.Permitir(RolesLocal.Empleada, permiso);
        }
        foreach (var permiso in PermisosCaja.Todos)
            matriz.Permitir(RolesLocal.Duena, permiso);
        foreach (var permiso in new[] { PermisosCaja.Abrir, PermisosCaja.Consultar, PermisosCaja.Movimiento, PermisosCaja.Cerrar })
            matriz.Permitir(RolesLocal.Empleada, permiso);
        matriz.Denegar(RolesLocal.Empleada, PermisosCaja.RetiroPropietario);
        matriz.Denegar(RolesLocal.Empleada, PermisosCaja.CorregirMedio);
        matriz.Permitir(RolesLocal.Duena, PermisosReportes.Ventas);
        matriz.Permitir(RolesLocal.Duena, PermisosReportes.Caja);
        matriz.Permitir(RolesLocal.Duena, PermisosReportes.Inventario);
        matriz.Permitir(RolesLocal.Duena, PermisosReportes.Costos);
        matriz.Denegar(RolesLocal.Empleada, PermisosReportes.Ventas);
        matriz.Denegar(RolesLocal.Empleada, PermisosReportes.Caja);
        matriz.Permitir(RolesLocal.Empleada, PermisosReportes.Inventario);
        matriz.Denegar(RolesLocal.Empleada, PermisosReportes.Costos);
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
