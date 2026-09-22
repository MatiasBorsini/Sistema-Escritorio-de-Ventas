using System.IO;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Resuelve una base de desarrollo elegida explícitamente. Nunca usa la ruta
/// provisional de forma silenciosa. Uso exclusivo de desarrollo, no productivo.
/// Precedencia inequívoca: argumento --dev-db prevalece sobre variable de entorno
/// LOCALINFANTIL_DEV_DB. No fija ni supone ruta productiva definitiva (D-13 pendiente).
/// Rechaza rutas equivalentes a la provisional (mayúsculas/minúsculas, separadores,
/// ruta relativa vs absoluta) sin modificar el archivo para comprobarlo.
/// </summary>
public static class SelectorBaseDesarrollo
{
    public const string EnvVar = "LOCALINFANTIL_DEV_DB";
    public const string ArgPrefijo = "--dev-db";

    public static string? Resolver(string[] args)
    {
        string? dev = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith(ArgPrefijo + "=", StringComparison.OrdinalIgnoreCase))
            {
                dev = a.Substring(ArgPrefijo.Length + 1).Trim().Trim('"');
            }
            else if (string.Equals(a, ArgPrefijo, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dev = args[i + 1].Trim().Trim('"');
                i++;
            }
        }

        // Precedencia: argumento manda sobre entorno
        if (string.IsNullOrWhiteSpace(dev))
        {
            dev = Environment.GetEnvironmentVariable(EnvVar);
        }

        if (string.IsNullOrWhiteSpace(dev))
        {
            return null;
        }

        var ruta = Normalizar(Path.GetFullPath(dev.Trim().Trim('"', '\'')));
        var provisional = Normalizar(Path.GetFullPath(RutaBaseDatosProvisional.ObtenerPredeterminada()));
        if (string.Equals(ruta, provisional, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La ruta de desarrollo no puede ser la base provisional del prototipo. Elegí otra ruta.");
        }

        return ruta;
    }

    private static string Normalizar(string ruta)
    {
        // Sin tocar el archivo: solo normalización sintáctica
        var n = ruta.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // En Windows, normalizar separadores a \ para comparación
        n = n.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return n;
    }
}
