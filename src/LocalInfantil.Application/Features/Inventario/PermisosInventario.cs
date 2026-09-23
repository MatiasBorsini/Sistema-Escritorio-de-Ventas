namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Permisos de inventario. Definidos pero NO concedidos a ningún rol en la
/// matriz productiva (D-03 pendiente). Solo cubre ESTABLECER existencia
/// inicial; la recepción que suma mercadería corresponde a F03.
/// </summary>
public static class PermisosInventario
{
    public const string CargaInicialEstablecer = "inventario.carga_inicial";
    public const string AjusteStock = "inventario.ajuste_stock";
    public const string ConteoRegistrar = "inventario.conteo_registrar";
    public const string ConteoAprobar = "inventario.conteo_aprobar";
    public const string NoVendibleGestionar = "inventario.no_vendible.gestionar";
    public const string BajaDefinitivaAutorizar = "inventario.baja_definitiva.autorizar";
}
