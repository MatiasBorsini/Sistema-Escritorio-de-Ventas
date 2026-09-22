using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioConteos : IRepositorioConteos
{
    private readonly IGeneradorIds _ids;

    public RepositorioConteos(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> CrearAsync(ISesionPersistencia sesion, string operacionId, string? creadoPorUsuarioId, string creadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO conteo (id, operacion_id, creado_por_usuario_id, creado_en) VALUES ($id, $operacion, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$usuario", (object?)creadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<ConteoRegistrado?> ObtenerAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_id, estado, aprobado_por_usuario_id FROM conteo WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ConteoRegistrado(
                lector.GetString(0),
                lector.GetString(1),
                lector.GetString(2),
                lector.IsDBNull(3) ? null : lector.GetString(3));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExistePorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM conteo WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task AgregarDetalleAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long existenciaEsperada, long movimientosContados, long cantidadContada, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO conteo_detalle (conteo_id, variante_id, existencia_esperada, movimientos_contados, cantidad_contada) VALUES ($conteo, $variante, $esperada, $contados, $contada);";
            comando.Parameters.AddWithValue("$conteo", conteoId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$esperada", existenciaEsperada);
            comando.Parameters.AddWithValue("$contados", movimientosContados);
            comando.Parameters.AddWithValue("$contada", cantidadContada);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarDetalleAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long cantidadContada, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE conteo_detalle SET cantidad_contada = $contada WHERE conteo_id = $conteo AND variante_id = $variante;";
            comando.Parameters.AddWithValue("$conteo", conteoId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$contada", cantidadContada);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<DetalleConteo>> ListarDetallesAsync(ISesionPersistencia sesion, string conteoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT variante_id, existencia_esperada, movimientos_contados, cantidad_contada FROM conteo_detalle WHERE conteo_id = $conteo ORDER BY variante_id;";
            comando.Parameters.AddWithValue("$conteo", conteoId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<DetalleConteo>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new DetalleConteo(lector.GetString(0), lector.GetInt64(1), lector.GetInt64(2), lector.GetInt64(3)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task AprobarAsync(ISesionPersistencia sesion, string id, string aprobadoPorUsuarioId, string aprobadoEn, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE conteo SET estado = 'aprobado', aprobado_por_usuario_id = $usuario, aprobado_en = $fecha WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$usuario", aprobadoPorUsuarioId);
            comando.Parameters.AddWithValue("$fecha", aprobadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<InformeConteoLinea>> InformeAsync(ISesionPersistencia sesion, string conteoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT d.variante_id, v.sku, d.existencia_esperada, d.cantidad_contada, d.cantidad_contada - d.existencia_esperada
                FROM conteo_detalle d JOIN variante v ON v.id = d.variante_id
                WHERE d.conteo_id = $conteo ORDER BY v.sku;
                """;
            comando.Parameters.AddWithValue("$conteo", conteoId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<InformeConteoLinea>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new InformeConteoLinea(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetInt64(2),
                    lector.GetInt64(3),
                    lector.GetInt64(4)));
            }

            return resultado;
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
