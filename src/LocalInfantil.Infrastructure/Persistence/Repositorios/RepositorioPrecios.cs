using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioPrecios : IRepositorioPrecios
{
    public async Task<string> FijarAsync(ISesionPersistencia sesion, NuevoPrecio datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = Guid.NewGuid().ToString("N");
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO precio (id, variante_id, importe_minor, moneda, vigente_desde, vigente_hasta, creado_en) VALUES ($id, $variante, $importe, $moneda, $desde, $hasta, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$variante", datos.VarianteId);
            comando.Parameters.AddWithValue("$importe", datos.ImporteMinor);
            comando.Parameters.AddWithValue("$moneda", datos.Moneda);
            comando.Parameters.AddWithValue("$desde", datos.VigenteDesde);
            comando.Parameters.AddWithValue("$hasta", (object?)datos.VigenteHasta ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<PrecioRegistrado?> ObtenerVigenteAsync(ISesionPersistencia sesion, string varianteId, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT id, variante_id, importe_minor, moneda, vigente_desde, vigente_hasta FROM precio
                WHERE variante_id = $variante AND vigente_desde <= $fecha AND (vigente_hasta IS NULL OR $fecha < vigente_hasta)
                ORDER BY vigente_desde DESC LIMIT 1;
                """;
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$fecha", fecha);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new PrecioRegistrado(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetInt64(2),
                lector.GetString(3),
                lector.GetString(4),
                lector.IsDBNull(5) ? null : lector.GetString(5));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static ISesionSqlite Sesion(ISesionPersistencia sesion)
    {
        if (sesion is not ISesionSqlite s)
        {
            throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));
        }

        return s;
    }

    private static void ExigirEscritura(ISesionSqlite sesion)
    {
        if (sesion.SoloLectura)
        {
            throw new InvalidOperationException("La sesión es de solo lectura y no admite escrituras.");
        }
    }
}
