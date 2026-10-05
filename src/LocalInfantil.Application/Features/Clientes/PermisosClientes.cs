namespace LocalInfantil.Application.Features.Clientes;

public static class PermisosClientes
{
    public const string Consultar = "clientes.consultar";
    public const string Administrar = "clientes.administrar";
    public static IReadOnlyList<string> Todos { get; } = [Consultar, Administrar];
}
