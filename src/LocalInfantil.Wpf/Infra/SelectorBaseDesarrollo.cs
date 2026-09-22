using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Wpf.Infra;

/// <summary>
/// Resuelve una base de desarrollo elegida explícitamente. Nunca usa la ruta
/// provisional de forma silenciosa. Uso exclusivo de desarrollo, no productivo.
/// Precedencia inequívoca: argumento --dev-db prevalece sobre variable de entorno
/// LOCALINFANTIL_DEV_DB. No fija ni supone ruta productiva definitiva (D-13 pendiente).
/// Rechaza rutas equivalentes a la provisional (mayúsculas/minúsculas, separadores,
/// ruta relativa vs absoluta) y, si ambos archivos existen, resuelve symlinks/
/// junctions del archivo y de sus directorios padre sin escribir para evitar alias
/// del sistema de archivos. Si el alias es de directorio y el archivo aún no existe,
/// la protección es solo sintáctica: limitación documentada, modo restringido a desarrollo.
/// </summary>
public static class SelectorBaseDesarrollo
{
    public const string EnvVar = "LOCALINFANTIL_DEV_DB";
    public const string ArgPrefijo = "--dev-db";

    public static string? Resolver(string[] args)
    {
        string? dev = null;
        var argProporcionado = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith(ArgPrefijo + "=", StringComparison.OrdinalIgnoreCase))
            {
                dev = a.Substring(ArgPrefijo.Length + 1).Trim().Trim('"', '\'');
                argProporcionado = true;
            }
            else if (string.Equals(a, ArgPrefijo, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                dev = args[i + 1].Trim().Trim('"', '\'');
                argProporcionado = true;
                i++;
            }
            else if (string.Equals(a, ArgPrefijo, StringComparison.OrdinalIgnoreCase))
            {
                // --dev-db sin valor -> explícito pero inválido
                dev = string.Empty;
                argProporcionado = true;
            }
        }

        if (!argProporcionado && string.IsNullOrWhiteSpace(dev))
        {
            dev = Environment.GetEnvironmentVariable(EnvVar);
            if (dev is not null && string.IsNullOrWhiteSpace(dev))
                throw new InvalidOperationException("La variable LOCALINFANTIL_DEV_DB está vacía.");
        }

        if (string.IsNullOrWhiteSpace(dev))
        {
            if (argProporcionado)
                throw new InvalidOperationException("Se proporcionó --dev-db sin ruta válida.");
            return null;
        }

        string ruta;
        try
        {
            ruta = Normalizar(Path.GetFullPath(dev!.Trim().Trim('"', '\'')));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("La ruta --dev-db es inválida.", ex);
        }

        var provisional = Normalizar(Path.GetFullPath(RutaBaseDatosProvisional.ObtenerPredeterminada()));
        if (EsMismaRuta(ruta, provisional))
        {
            throw new InvalidOperationException(
                "La ruta de desarrollo no puede ser la base provisional del prototipo. Elegí otra ruta.");
        }

        // Rechazo conservador: cualquier directorio antecesor con reparse point no resuelto
        if (TieneReparseEnAntecesores(ruta))
        {
            throw new InvalidOperationException(
                "La ruta de desarrollo contiene un enlace (junction/symlink) en sus directorios antecesores que no puede resolverse con seguridad. Usá una ruta sin reparse points.");
        }

        return ruta;
    }

    private static string Normalizar(string ruta)
    {
        var n = ruta.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        n = n.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return n;
    }

    private static bool EsMismaRuta(string ruta, string provisional)
    {
        if (string.Equals(ruta, provisional, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            if (File.Exists(ruta) && File.Exists(provisional))
            {
                // Hard links: mismo VolumeSerial + FileIndex
                if (SonMismoArchivoFisico(ruta, provisional))
                    return true;
                var r1 = ResolverFinal(ruta);
                var r2 = ResolverFinal(provisional);
                if (string.Equals(r1, r2, StringComparison.OrdinalIgnoreCase))
                    return true;
                // También comparar todos los antecesores resueltos
                var a1 = ResolverTodosAntecesores(ruta);
                var a2 = ResolverTodosAntecesores(provisional);
                if (string.Equals(a1, a2, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // Archivo dev aún no existe: verificar si comparten directorio final resuelto y mismo nombre
                var dirDev = Path.GetDirectoryName(ruta);
                var dirProv = Path.GetDirectoryName(provisional);
                if (dirDev is not null && dirProv is not null)
                {
                    var d1 = ResolverTodosAntecesores(ruta);
                    var d2 = ResolverTodosAntecesores(provisional);
                    if (string.Equals(d1, d2, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static string ResolverTodosAntecesores(string ruta)
    {
        // Resuelve symlinks/junctions de cada segmento antecesor
        var dir = Path.GetDirectoryName(ruta);
        var file = Path.GetFileName(ruta);
        if (dir is null) return Normalizar(Path.GetFullPath(ruta));
        var dirResuelto = ResolverFinalDirectorioRecursivo(dir);
        return Normalizar(Path.Combine(dirResuelto, file));
    }

    private static string ResolverFinal(string ruta)
    {
        var info = new FileInfo(ruta);
        var target = info.ResolveLinkTarget(true);
        if (target is not null)
            return Normalizar(Path.GetFullPath(target.FullName));
        return Normalizar(Path.GetFullPath(ruta));
    }

    private static string ResolverFinalDirectorio(string dir)
    {
        var info = new DirectoryInfo(dir);
        var target = info.ResolveLinkTarget(true);
        if (target is not null)
            return Normalizar(Path.GetFullPath(target.FullName));
        return Normalizar(Path.GetFullPath(dir));
    }

    private static string ResolverFinalDirectorioRecursivo(string dir)
    {
        var actual = Normalizar(Path.GetFullPath(dir));
        // Recorrer cada antecesor y resolver si es reparse
        var partes = actual.Split(Path.DirectorySeparatorChar);
        var construido = partes[0] + Path.DirectorySeparatorChar;
        for (int i = 1; i < partes.Length; i++)
        {
            if (string.IsNullOrEmpty(partes[i])) continue;
            construido = Path.Combine(construido, partes[i]);
            if (Directory.Exists(construido))
            {
                var info = new DirectoryInfo(construido);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var target = info.ResolveLinkTarget(true);
                    if (target is not null)
                        construido = Normalizar(Path.GetFullPath(target.FullName));
                }
            }
        }
        return construido;
    }

    private static bool TieneReparseEnAntecesores(string ruta)
    {
        var dir = Path.GetDirectoryName(ruta);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir))
            {
                var info = new DirectoryInfo(dir);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    private static bool SonMismoArchivoFisico(string ruta1, string ruta2)
    {
        try
        {
            using var h1 = CreateFile(ruta1, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            using var h2 = CreateFile(ruta2, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (h1.IsInvalid || h2.IsInvalid) return false;
            if (!GetFileInformationByHandle(h1, out var info1)) return false;
            if (!GetFileInformationByHandle(h2, out var info2)) return false;
            return info1.VolumeSerialNumber == info2.VolumeSerialNumber
                && info1.FileIndexHigh == info2.FileIndexHigh
                && info1.FileIndexLow == info2.FileIndexLow;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, FileShare dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
