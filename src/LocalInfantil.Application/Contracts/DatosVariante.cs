namespace LocalInfantil.Application.Contracts;

public sealed record NuevaVariante(string ProductoId, string? Talle, string? Color);

public sealed record VarianteRegistrada(string Id, string ProductoId, string CodigoInterno, string? Talle, string? Color, bool Activa);

/// <summary>
/// Sugerencia de variante para typeahead: identificación interna más contexto
/// humano (producto, marca/categoría, talle/color). Todo sale de columnas
/// existentes en una sola lectura; sin precio ni stock.
/// </summary>
public sealed record SugerenciaVariante(
    string VarianteId,
    string ProductoId,
    string CodigoInterno,
    string NombreProducto,
    string? Marca,
    string? Categoria,
    string? Talle,
    string? Color,
    bool Activa)
{
    /// <summary>Línea secundaria humana (ej: "Nube · Niños"; null si no hay).</summary>
    public string? ContextoProducto => (Marca, Categoria) switch
    {
        (null, null) => null,
        (null, var c) => c!,
        (var m, null) => m!,
        (var m, var c) => $"{m} · {c}"
    };

    /// <summary>Detalle de variante ("M · Rojo").</summary>
    public string DetalleTalleColor => (Talle, Color) switch
    {
        (null, null) => "—",
        (null, var c) => c!,
        (var t, null) => t!,
        (var t, var c) => $"{t} · {c}"
    };

    /// <summary>Subtítulo modo producto: contexto o código+detalle si es match por código.</summary>
    public string SubProducto => ContextoProducto ?? $"{CodigoInterno} · {DetalleTalleColor}";
}
