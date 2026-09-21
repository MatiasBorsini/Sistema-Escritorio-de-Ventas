namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Contenido ficticio e independiente de WPF para comprobar el flujo de impresión.
/// </summary>
public sealed record DocumentoImpresionDePrueba(
    string NombreLocal,
    DateTime Fecha,
    IReadOnlyList<LineaDocumentoImpresionDePrueba> Lineas,
    decimal Total);

public sealed record LineaDocumentoImpresionDePrueba(
    string Descripcion,
    int Cantidad,
    decimal ImporteUnitario,
    decimal ImporteSubtotal);
