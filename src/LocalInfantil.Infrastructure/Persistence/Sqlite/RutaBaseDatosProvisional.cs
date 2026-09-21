namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Centraliza la ruta solo para el prototipo. No constituye la decisión de producción pendiente D-13.
/// </summary>
public static class RutaBaseDatosProvisional
{
    public static string ObtenerPredeterminada()
    {
        var datosLocales = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(datosLocales, "LocalInfantil", "Prototype", "localinfantil-prototype.db");
    }
}
