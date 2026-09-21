namespace LocalInfantil.Application.Contracts;

public sealed record NuevaVariante(string ProductoId, string Sku, string? Talle, string? Color);

public sealed record VarianteRegistrada(string Id, string ProductoId, string Sku, string? Talle, string? Color, bool Activa);
