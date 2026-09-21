namespace LocalInfantil.Application.Contracts;

public sealed record NuevoPrecio(string VarianteId, long ImporteMinor, string Moneda, string VigenteDesde, string? VigenteHasta);

public sealed record PrecioRegistrado(string Id, string VarianteId, long ImporteMinor, string Moneda, string VigenteDesde, string? VigenteHasta);
