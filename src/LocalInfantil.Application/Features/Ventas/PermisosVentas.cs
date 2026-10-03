namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Permisos de ventas POS. Definidos pero NO concedidos en la matriz
/// productiva salvo decisión explícita (D-03 pendiente): por defecto se
/// deniegan. Las pruebas los conceden en matrices propias.
/// </summary>
public static class PermisosVentas
{
    public const string VentasConfirmar = "ventas.confirmar";
    public const string VentasAnular = "ventas.anular";
}
