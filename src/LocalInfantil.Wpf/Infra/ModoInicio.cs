using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Destino de arranque de la aplicación.
/// Produccion es el modo normal sin argumentos (D-45); Desarrollo exige
/// --dev-db explícito; PrototipoExplicito abre la sonda histórica solo con
/// --prototype (diagnóstico, sin migraciones).
/// </summary>
public enum DestinoInicio
{
    Produccion,
    Desarrollo,
    PrototipoExplicito,
    Error,
}

public sealed record ResolucionInicio(DestinoInicio Destino, string? RutaBase, string? Error);

/// <summary>
/// Decide el modo de arranque sin efectos secundarios (testeable).
/// Sin argumentos jamás resuelve la base histórica Prototype.
/// </summary>
public static class ModoInicio
{
    public const string ArgPrototipo = "--prototype";

    public static ResolucionInicio Resolver(string[] args)
    {
        string? rutaDev;
        try
        {
            rutaDev = SelectorBaseDesarrollo.Resolver(args);
        }
        catch (Exception ex)
        {
            return new ResolucionInicio(DestinoInicio.Error, null, ex.Message);
        }

        if (rutaDev is not null)
            return new ResolucionInicio(DestinoInicio.Desarrollo, rutaDev, null);

        foreach (var a in args)
        {
            if (string.Equals(a, ArgPrototipo, StringComparison.OrdinalIgnoreCase))
                return new ResolucionInicio(DestinoInicio.PrototipoExplicito, RutaBaseDatosProvisional.ObtenerPredeterminada(), null);
        }

        return new ResolucionInicio(DestinoInicio.Produccion, RutaBaseDatosProductiva.ObtenerBaseDatos(), null);
    }
}
