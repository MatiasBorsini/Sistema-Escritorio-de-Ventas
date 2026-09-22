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
/// ruta relativa vs absoluta) y, si ambos archivos existen, verifica identidad física
/// (hard links) y resuelve symlinks/junctions del archivo y de todos sus directorios
/// antecesores sin escribir el contenido: abre handles de solo metadatos (CreateFile
/// con acceso cero) para consultar FileIndex, sin leer/modificar SQLite. Si no puede
/// establecerse con seguridad que no es alias, falla cerrado. Directorios con reparse
/// en antecesores se rechazan conservadoramente. Modo restringido a desarrollo.
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

        var rutaExiste = File.Exists(ruta);
        var provExiste = File.Exists(provisional);
        try
        {
            if (rutaExiste && provExiste)
            {
                // Falla cerrado si no puede comprobar hard link
                bool mismoFisico;
                try { mismoFisico = SonMismoArchivoFisico(ruta, provisional); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo verificar identidad física de la base provisional.", ex); }
                if (mismoFisico) return true;

                string r1, r2;
                try { r1 = ResolverFinal(ruta); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver alias de la ruta de desarrollo.", ex); }
                try { r2 = ResolverFinal(provisional); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver alias de la base provisional.", ex); }
                if (string.Equals(r1, r2, StringComparison.OrdinalIgnoreCase)) return true;

                string a1, a2;
                try { a1 = ResolverTodosAntecesores(ruta); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver antecesores de la ruta de desarrollo.", ex); }
                try { a2 = ResolverTodosAntecesores(provisional); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver antecesores de la base provisional.", ex); }
                if (string.Equals(a1, a2, StringComparison.OrdinalIgnoreCase)) return true;
            }
            else
            {
                string d1, d2;
                try { d1 = ResolverTodosAntecesores(ruta); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver antecesores de la ruta de desarrollo.", ex); }
                try { d2 = ResolverTodosAntecesores(provisional); }
                catch (Exception ex) { throw new InvalidOperationException("No se pudo resolver antecesores de la base provisional.", ex); }
                if (string.Equals(d1, d2, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("No se pudo verificar si la ruta es alias de la provisional.", ex);
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
        SafeFileHandle h1 = null!;
        SafeFileHandle h2 = null!;
        try
        {
            h1 = CreateFile(ruta1, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            h2 = CreateFile(ruta2, 0, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
            if (h1.IsInvalid || h2.IsInvalid)
                throw new InvalidOperationException($"No se pudo abrir handle para comparar identidad física (provisional o dev).");
            if (!GetFileInformationByHandle(h1, out var info1))
                throw new InvalidOperationException("No se pudo consultar metadatos de la ruta de desarrollo.");
            if (!GetFileInformationByHandle(h2, out var info2))
                throw new InvalidOperationException("No se pudo consultar metadatos de la base provisional.");
            return info1.VolumeSerialNumber == info2.VolumeSerialNumber
                && info1.FileIndexHigh == info2.FileIndexHigh
                && info1.FileIndexLow == info2.FileIndexLow;
        }
        finally
        {
            h1?.Dispose();
            h2?.Dispose();
        }
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
