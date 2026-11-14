namespace LocalInfantil.Application.Contracts;

public sealed record CombinacionVariante(string? Talle, string? Color);

public sealed record PropuestaVarianteBatch(
    string? CodigoInterno,
    string? Talle,
    string? Color,
    bool YaExiste);

public sealed record ResultadoVariantesBatch(IReadOnlyList<string> IdsCreados)
{
    public int CantidadCreadas => IdsCreados.Count;
}
