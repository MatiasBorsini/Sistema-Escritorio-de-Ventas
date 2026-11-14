using System.IO;
using System.Reflection;
using System.Resources;

namespace LocalInfantil.Wpf.Infra;

/// <summary>Acceso único a los bytes del logo empaquetado como recurso WPF.</summary>
public static class LogoNegocio
{
    private const string NombreContenedorRecursos = "LocalInfantil.Wpf.g.resources";
    private const string NombreRecursoLogo = "assets/branding/logo-tina.png";
    private const string NombreRecursoBanner = "assets/branding/logo.png";
    private static readonly Lazy<byte[]> Png = new(() => CargarPng(NombreRecursoLogo));
    private static readonly Lazy<byte[]> BannerPng = new(() => CargarPng(NombreRecursoBanner));

    public static byte[] ObtenerPng() => Png.Value.ToArray();
    public static byte[] ObtenerBannerPng() => BannerPng.Value.ToArray();

    private static byte[] CargarPng(string nombreRecurso)
    {
        var ensamblado = typeof(LogoNegocio).GetTypeInfo().Assembly;
        var flujoRecursos = ensamblado.GetManifestResourceStream(NombreContenedorRecursos)
            ?? throw new InvalidOperationException("No se encontró el contenedor de recursos de la aplicación.");
        using var lector = new ResourceReader(flujoRecursos);
        var recursos = lector.GetEnumerator();
        while (recursos.MoveNext())
        {
            if (!string.Equals(recursos.Key as string, nombreRecurso, StringComparison.OrdinalIgnoreCase)
                || recursos.Value is not Stream flujoPng)
                continue;

            using (flujoPng)
            using (var memoria = new MemoryStream())
            {
                flujoPng.CopyTo(memoria);
                return memoria.ToArray();
            }
        }

        throw new InvalidOperationException($"No se encontró el recurso de identidad '{nombreRecurso}'.");
    }
}
