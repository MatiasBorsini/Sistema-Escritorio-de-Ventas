namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Permisos de inventario. Definidos pero NO concedidos a ningún rol en la
/// matriz productiva (D-03 pendiente). Solo cubre ESTABLECER existencia
/// inicial; la recepción que suma mercadería corresponde a F03.
/// </summary>
public static class PermisosInventario
{
    public const string CargaInicialEstablecer = "inventario.carga_inicial";
}
