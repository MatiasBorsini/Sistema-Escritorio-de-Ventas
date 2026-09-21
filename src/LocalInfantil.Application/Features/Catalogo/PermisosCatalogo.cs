namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Permisos de catálogo. Definidos pero NO concedidos a ningún rol en la
/// matriz productiva (D-03 pendiente): por defecto se deniegan. Las pruebas
/// los conceden en matrices propias para demostrar el mecanismo.
/// </summary>
public static class PermisosCatalogo
{
    public const string ProductosGestionar = "catalogo.productos.gestionar";
    public const string VariantesGestionar = "catalogo.variantes.gestionar";
    public const string PreciosGestionar = "catalogo.precios.gestionar";
}
