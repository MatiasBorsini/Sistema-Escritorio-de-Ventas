namespace LocalInfantil.Application.Contracts;

public sealed record VarianteParaEtiqueta(
    string VarianteId,
    string CodigoInterno,
    string NombreProducto,
    string? Talle,
    string? Color,
    long Disponible,
    long? PrecioMinor,
    string? Moneda);

public sealed record SeleccionEtiqueta(VarianteParaEtiqueta Variante, int Cantidad);
