namespace LocalInfantil.Application.Features.Reportes;

/// <summary>
/// Permisos de reportes. Solo lectura agregada; deny-by-default. No hay
/// pantalla ni funcionalidad de costos hasta que se apruebe D-07, pero el
/// permiso existe para reservar el área.
/// </summary>
public static class PermisosReportes
{
    public const string Ventas = "reportes.ventas";
    public const string Caja = "reportes.caja";
    public const string Inventario = "reportes.inventario";
    public const string Costos = "reportes.costos";

    public static readonly IReadOnlyList<string> Todos = new[]
    {
        Ventas, Caja, Inventario, Costos,
    };
}
