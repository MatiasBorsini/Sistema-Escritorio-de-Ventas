namespace LocalInfantil.Application.Features.Respaldos;

public static class PermisosRespaldos
{
    public const string Consultar="respaldos.consultar";
    public const string Crear="respaldos.crear";
    public const string Configurar="respaldos.configurar";
    public const string Restaurar="respaldos.restaurar";
    public static IReadOnlyList<string> Todos {get;}=[Consultar,Crear,Configurar,Restaurar];
}
