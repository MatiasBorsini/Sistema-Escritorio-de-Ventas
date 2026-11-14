namespace LocalInfantil.Application.Features.Cambios;

public static class PermisosCambios
{
    public const string Registrar = "cambios.registrar";
    public const string Consultar = "cambios.consultar";
    public static IReadOnlyList<string> Todos { get; } = [Registrar, Consultar];
}
