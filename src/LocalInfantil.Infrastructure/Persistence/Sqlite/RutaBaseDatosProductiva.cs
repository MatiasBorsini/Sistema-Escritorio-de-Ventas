namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Rutas productivas bajo %LOCALAPPDATA%\LocalInfantil (D-45). Se resuelven
/// con Environment.SpecialFolder.LocalApplicationData: nunca se hardcodea el
/// nombre del usuario. Válido para una sola PC con cuenta Windows compartida;
/// si algún día hubiera varias cuentas, esta decisión debe revisarse.
/// Estructura separada: Data (DB comercial), Backups, Secrets, Updates.
/// Nunca junto al exe, ni en Program Files, ni en carpetas sincronizadas
/// (OneDrive/Google Drive).
/// </summary>
public static class RutaBaseDatosProductiva
{
    public const string NombreBase = "localinfantil.db";

    public static string ObtenerRaiz()
    {
        var datosLocales = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(datosLocales, "LocalInfantil");
    }

    public static string ObtenerBaseDatos()
    {
        return Path.Combine(ObtenerRaiz(), "Data", NombreBase);
    }

    public static string ObtenerDirDatos()
    {
        return Path.Combine(ObtenerRaiz(), "Data");
    }

    public static string ObtenerDirRespaldos()
    {
        return Path.Combine(ObtenerRaiz(), "Backups");
    }

    public static string ObtenerDirSecretos()
    {
        return Path.Combine(ObtenerRaiz(), "Secrets");
    }

    public static string ObtenerDirActualizaciones()
    {
        return Path.Combine(ObtenerRaiz(), "Updates");
    }
}
