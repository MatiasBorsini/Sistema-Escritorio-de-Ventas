namespace LocalInfantil.Wpf.Printing;

/// <summary>
/// Acumula todas las páginas de un reporte antes de imprimir. Si hay más
/// registros que el tope, falla explícitamente: nunca se entrega un documento
/// truncado sin aviso.
/// </summary>
public static class PaginacionReporte
{
    public const int TamanoPagina = 100;
    public const int MaximoPaginas = 200;
    public const int MaximoRegistros = TamanoPagina * MaximoPaginas;

    public static async Task<List<T>> TodasLasPaginasAsync<T>(
        Func<int, int, Task<IReadOnlyList<T>>> pagina)
    {
        ArgumentNullException.ThrowIfNull(pagina);

        var todos = new List<T>();
        for (var numeroPagina = 0; numeroPagina < MaximoPaginas; numeroPagina++)
        {
            var lote = await pagina(TamanoPagina, numeroPagina * TamanoPagina);
            todos.AddRange(lote.Take(TamanoPagina));
            if (lote.Count <= TamanoPagina) return todos;
        }

        throw new InvalidOperationException(
            $"El reporte supera el máximo de {MaximoRegistros:N0} registros ({MaximoPaginas} páginas). " +
            "Acotá el período o los filtros y volvé a intentar. No se envió nada a la impresora.");
    }
}
