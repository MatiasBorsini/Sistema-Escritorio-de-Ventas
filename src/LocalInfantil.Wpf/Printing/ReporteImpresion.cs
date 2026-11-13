namespace LocalInfantil.Wpf.Printing;

public enum OrientacionReporte
{
    Vertical,
    Horizontal,
}

public sealed record ColumnaReporte(string Titulo, double Peso, bool AlineadaDerecha = false);

public sealed record TablaReporte(
    string Titulo,
    IReadOnlyList<ColumnaReporte> Columnas,
    IReadOnlyList<IReadOnlyList<string>> Filas);

/// <summary>
/// Datos listos para imprimir. Se construyen en el ViewModel a partir de las
/// consultas de solo lectura; el renderer no calcula importes ni modifica
/// resultados comerciales.
/// </summary>
public sealed record ReporteImpresion
{
    public required string Titulo { get; init; }
    public required string Filtros { get; init; }
    public string? Nota { get; init; }
    public DateTimeOffset Emitido { get; init; } = DateTimeOffset.Now;
    public OrientacionReporte Orientacion { get; init; } = OrientacionReporte.Vertical;
    public IReadOnlyList<(string Etiqueta, string Valor)> Resumen { get; init; } = [];
    public IReadOnlyList<TablaReporte> Tablas { get; init; } = [];
    public string Pie { get; init; } = "LocalInfantil · TiNA kids · Documento no fiscal";
}
