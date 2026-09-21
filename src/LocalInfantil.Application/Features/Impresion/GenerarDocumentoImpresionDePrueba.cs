using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Impresion;

/// <summary>
/// Genera contenido fijo de demostración; no representa una venta ni se persiste.
/// </summary>
public sealed class GenerarDocumentoImpresionDePrueba
{
    private readonly TimeProvider _timeProvider;

    public GenerarDocumentoImpresionDePrueba(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DocumentoImpresionDePrueba Generar()
    {
        var lineas = new[]
        {
            CrearLinea("Remera estampada (ejemplo)", 2, 12_500m),
            CrearLinea("Pantalón de algodón (ejemplo)", 1, 18_900m),
            CrearLinea("Medias infantiles (ejemplo)", 3, 3_200m)
        };

        return new DocumentoImpresionDePrueba(
            NombreLocal: "Local Infantil — documento de prueba",
            Fecha: _timeProvider.GetLocalNow().DateTime,
            Lineas: lineas,
            Total: lineas.Sum(linea => linea.ImporteSubtotal));
    }

    private static LineaDocumentoImpresionDePrueba CrearLinea(string descripcion, int cantidad, decimal importeUnitario)
    {
        return new LineaDocumentoImpresionDePrueba(
            Descripcion: descripcion,
            Cantidad: cantidad,
            ImporteUnitario: importeUnitario,
            ImporteSubtotal: cantidad * importeUnitario);
    }
}
