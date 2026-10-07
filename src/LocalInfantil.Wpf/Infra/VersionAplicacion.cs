using System.Reflection;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Versión visible de la aplicación desde una única fuente: el
/// &lt;Version&gt; del csproj (AssemblyInformationalVersion). Sin textos
/// hardcodeados dispersos.
/// </summary>
public static class VersionAplicacion
{
    public static string Obtener()
    {
        return ObtenerDesde(Assembly.GetEntryAssembly());
    }

    public static string ObtenerDesde(Assembly? ensamblado)
    {
        var informativo = ensamblado?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informativo))
        {
            var sinSufijo = informativo.Split('+')[0];
            if (!string.IsNullOrWhiteSpace(sinSufijo))
                return sinSufijo;
        }

        var version = ensamblado?.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
