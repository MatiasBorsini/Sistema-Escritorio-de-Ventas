using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioApartados : IRepositorioApartados
{
    private readonly IGeneradorIds _ids;

    public RepositorioApartados(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> RegistrarAsync(
        ISesionPersistencia sesion,
        string varianteId,
        string tipo,
        long cantidad,
        string motivo,
        string operacionId,
        string? creadoPorUsuarioId,
        string creadoEn,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO apartado_movimiento (id, variante_id, tipo, cantidad, motivo, operacion_id, creado_por_usuario_id, creado_en) VALUES ($id, $variante, $tipo, $cantidad, $motivo, $operacion, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$tipo", tipo);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$motivo", motivo);
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

    public async Task<long> SumarApartadoPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COALESCE(SUM(CASE WHEN tipo = 'apartar' THEN cantidad ELSE -cantidad END), 0) FROM apartado_movimiento WHERE variante_id = $variante;";
            comando.Parameters.AddWithValue("$variante", varianteId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<long> ContarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COUNT(*) FROM apartado_movimiento WHERE variante_id = $variante;";
            comando.Parameters.AddWithValue("$variante", varianteId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken));
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
            comando.CommandText = "SELECT 1 FROM apartado_movimiento WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ApartadoHistorial>> ListarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        if (limite < 1 || limite > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limite), "El límite debe estar entre 1 y 100.");
        }

        if (desplazamiento < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(desplazamiento), "El desplazamiento no puede ser negativo.");
        }

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT m.id, m.tipo, m.cantidad, m.motivo, u.nombre, m.operacion_id, m.creado_en
                FROM apartado_movimiento m LEFT JOIN usuario u ON u.id = m.creado_por_usuario_id
                WHERE m.variante_id = $variante ORDER BY m.creado_en, m.id LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$limite", limite);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ApartadoHistorial>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new ApartadoHistorial(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetInt64(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.GetString(5),
                    lector.GetString(6)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task GuardarFotoConteoAsync(ISesionPersistencia sesion, string conteoId, string varianteId, long apartadoEsperado, long clasificacionesContadas, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO conteo_clasificacion (conteo_id, variante_id, apartado_esperado, clasificaciones_contadas) VALUES ($conteo, $variante, $esperado, $contados);";
            comando.Parameters.AddWithValue("$conteo", conteoId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$esperado", apartadoEsperado);
            comando.Parameters.AddWithValue("$contados", clasificacionesContadas);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<FotoClasificacion?> ObtenerFotoConteoAsync(ISesionPersistencia sesion, string conteoId, string varianteId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT variante_id, apartado_esperado, clasificaciones_contadas FROM conteo_clasificacion WHERE conteo_id = $conteo AND variante_id = $variante;";
            comando.Parameters.AddWithValue("$conteo", conteoId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new FotoClasificacion(lector.GetString(0), lector.GetInt64(1), lector.GetInt64(2));
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
