using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioRecepciones : IRepositorioRecepciones
{
    private readonly IGeneradorIds _ids;

    public RepositorioRecepciones(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string proveedorId,
        string operacionId,
        string? comprobanteRef,
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
            comando.CommandText = "INSERT INTO recepcion (id, proveedor_id, operacion_id, comprobante_ref, creado_por_usuario_id, creado_en) VALUES ($id, $proveedor, $operacion, $comprobante, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$proveedor", proveedorId);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$comprobante", (object?)comprobanteRef ?? DBNull.Value);
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

    public async Task InsertarLineaAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string varianteId,
        long cantidad,
        long? costoUnitarioMinor,
        string? moneda,
        CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO recepcion_linea (id, recepcion_id, variante_id, cantidad, costo_unitario_minor, moneda) VALUES ($id, $recepcion, $variante, $cantidad, $costo, $moneda);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            comando.Parameters.AddWithValue("$variante", varianteId);
            comando.Parameters.AddWithValue("$cantidad", cantidad);
            comando.Parameters.AddWithValue("$costo", (object?)costoUnitarioMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$moneda", (object?)moneda ?? DBNull.Value);
            await comando.ExecuteNonQueryAsync(cancellationToken);
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
            comando.CommandText = "SELECT 1 FROM recepcion WHERE operacion_id = $operacion LIMIT 1;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool> ExisteReversionAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT 1 FROM reversion_recepcion WHERE recepcion_id = $recepcion LIMIT 1;";
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            return await comando.ExecuteScalarAsync(cancellationToken) is not null;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task InsertarReversionAsync(
        ISesionPersistencia sesion,
        string recepcionId,
        string operacionId,
        string motivo,
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
            comando.CommandText = "INSERT INTO reversion_recepcion (id, recepcion_id, operacion_id, motivo, creado_por_usuario_id, creado_en) VALUES ($id, $recepcion, $operacion, $motivo, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            comando.Parameters.AddWithValue("$operacion", operacionId);
            comando.Parameters.AddWithValue("$motivo", motivo);
            comando.Parameters.AddWithValue("$usuario", (object?)creadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(ISesionPersistencia sesion, string recepcionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT variante_id, cantidad, costo_unitario_minor, moneda FROM recepcion_linea WHERE recepcion_id = $recepcion ORDER BY variante_id;";
            comando.Parameters.AddWithValue("$recepcion", recepcionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<LineaRecepcion>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new LineaRecepcion(
                    lector.GetString(0),
                    lector.GetInt64(1),
                    lector.IsDBNull(2) ? null : lector.GetInt64(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3)));
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
