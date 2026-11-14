namespace LocalInfantil.Wpf.Printing;

public sealed record EtiquetasOpciones(double AnchoMm, double AltoMm, double MargenHorizontalMm, double MargenVerticalMm, double SeparacionHorizontalMm, double SeparacionVerticalMm, bool MostrarPrecio);

public sealed record DistribucionEtiquetas(int Columnas, int FilasPorPagina, int EtiquetasPorPagina, int Paginas, int TotalEtiquetas)
{
    public static DistribucionEtiquetas Calcular(int cantidad, double anchoPaginaMm, double altoPaginaMm, EtiquetasOpciones opciones)
    {
        Validar(opciones);
        var anchoUtil = anchoPaginaMm - 2 * opciones.MargenHorizontalMm;
        var altoUtil = altoPaginaMm - 2 * opciones.MargenVerticalMm;
        var columnas = (int)Math.Floor((anchoUtil + opciones.SeparacionHorizontalMm) / (opciones.AnchoMm + opciones.SeparacionHorizontalMm));
        var filas = (int)Math.Floor((altoUtil + opciones.SeparacionVerticalMm) / (opciones.AltoMm + opciones.SeparacionVerticalMm));
        if (columnas < 1 || filas < 1) throw new InvalidOperationException("La etiqueta no cabe en la página configurada.");
        var porPagina = checked(columnas * filas);
        return new(columnas, filas, porPagina, cantidad == 0 ? 0 : (int)Math.Ceiling(cantidad / (double)porPagina), cantidad);
    }

    public static void Validar(EtiquetasOpciones o)
    {
        if (!double.IsFinite(o.AnchoMm) || !double.IsFinite(o.AltoMm) || o.AnchoMm <= 0 || o.AltoMm <= 0 || o.MargenHorizontalMm < 0 || o.MargenVerticalMm < 0 || o.SeparacionHorizontalMm < 0 || o.SeparacionVerticalMm < 0)
            throw new InvalidOperationException("Revisá las medidas: tamaño debe ser positivo y márgenes/separaciones no negativos.");
    }
}
