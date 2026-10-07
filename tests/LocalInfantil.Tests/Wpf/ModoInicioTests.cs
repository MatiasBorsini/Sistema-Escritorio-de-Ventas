using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Contrato de arranque (D-44): sin argumentos SIEMPRE producción;
/// --dev-db exige desarrollo explícito; Prototype jamás se abre solo.
/// </summary>
public sealed class ModoInicioTests
{
    [Fact]
    public void Sin_argumentos_es_produccion_en_data()
    {
        var r = ModoInicio.Resolver([]);
        Assert.Equal(DestinoInicio.Produccion, r.Destino);
        Assert.NotNull(r.RutaBase);
        Assert.EndsWith(Path.Combine("Data", RutaBaseDatosProductiva.NombreBase), r.RutaBase!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sin_argumentos_jamas_resuelve_prototype()
    {
        var r = ModoInicio.Resolver([]);
        var provisional = RutaBaseDatosProvisional.ObtenerPredeterminada();
        Assert.DoesNotContain("Prototype", r.RutaBase, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(
            Path.GetFullPath(provisional).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(r.RutaBase!).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Dev_db_resuelve_desarrollo()
    {
        var r = ModoInicio.Resolver(["--dev-db", "C:/tmp/dev-test.db"]);
        Assert.Equal(DestinoInicio.Desarrollo, r.Destino);
        Assert.NotNull(r.RutaBase);
    }

    [Fact]
    public void Dev_db_sin_valor_es_error()
    {
        var r = ModoInicio.Resolver(["--dev-db"]);
        Assert.Equal(DestinoInicio.Error, r.Destino);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Dev_db_hacia_prototype_es_error()
    {
        var r = ModoInicio.Resolver(["--dev-db", RutaBaseDatosProvisional.ObtenerPredeterminada()]);
        Assert.Equal(DestinoInicio.Error, r.Destino);
    }

    [Fact]
    public void Prototype_explicito_abre_solo_diagnostico()
    {
        var r = ModoInicio.Resolver(["--prototype"]);
        Assert.Equal(DestinoInicio.PrototipoExplicito, r.Destino);
        Assert.Equal(
            Path.GetFullPath(RutaBaseDatosProvisional.ObtenerPredeterminada()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(r.RutaBase!).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Ruta_productiva_separa_datos_backups_secretos_updates()
    {
        var raiz = RutaBaseDatosProductiva.ObtenerRaiz();
        Assert.Equal(
            Path.Combine(raiz, "Data", RutaBaseDatosProductiva.NombreBase),
            RutaBaseDatosProductiva.ObtenerBaseDatos());
        Assert.StartsWith(
            raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            RutaBaseDatosProductiva.ObtenerDirRespaldos());
        Assert.StartsWith(
            raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            RutaBaseDatosProductiva.ObtenerDirSecretos());
        Assert.StartsWith(
            raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            RutaBaseDatosProductiva.ObtenerDirActualizaciones());
        Assert.DoesNotContain("Prototype", RutaBaseDatosProductiva.ObtenerBaseDatos(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ruta_productiva_deriva_de_specialfolder_sin_hardcode()
    {
        var baseDatos = Path.GetFullPath(RutaBaseDatosProductiva.ObtenerBaseDatos());
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Assert.StartsWith(localAppData, baseDatos, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LocalInfantil", baseDatos);
    }

    [Fact]
    public void Ruta_productiva_no_esta_junto_al_exe_ni_en_program_files()
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty;
        var baseDatos = Path.GetFullPath(RutaBaseDatosProductiva.ObtenerBaseDatos());
        Assert.False(baseDatos.StartsWith(exeDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFiles))
            Assert.False(baseDatos.StartsWith(programFiles.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(programFilesX86))
            Assert.False(baseDatos.StartsWith(programFilesX86.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
