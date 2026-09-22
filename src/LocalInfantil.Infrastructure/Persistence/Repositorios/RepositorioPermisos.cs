using LocalInfantil.Application.Abstractions;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioPermisos : IRepositorioPermisos
{
    public async Task DefinirAsync(ISesionPersistencia sesion, string usuarioId, string permiso, bool concedido, string actualizadoEn, string? actualizadoPorUsuarioId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO permiso_usuario (usuario_id, permiso, concedido, actualizado_en, actualizado_por_usuario_id) VALUES ($usuario, $permiso, $concedido, $actualizadoEn, $actualizadoPor) ON CONFLICT (usuario_id, permiso) DO UPDATE SET concedido = $concedido, actualizado_en = $actualizadoEn, actualizado_por_usuario_id = $actualizadoPor;";
            comando.Parameters.AddWithValue("$usuario", usuarioId);
            comando.Parameters.AddWithValue("$permiso", permiso);
            comando.Parameters.AddWithValue("$concedido", concedido ? 1 : 0);
            comando.Parameters.AddWithValue("$actualizadoEn", actualizadoEn);
            comando.Parameters.AddWithValue("$actualizadoPor", (object?)actualizadoPorUsuarioId ?? DBNull.Value);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<bool?> ObtenerAsync(ISesionPersistencia sesion, string usuarioId, string permiso, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT concedido FROM permiso_usuario WHERE usuario_id = $usuario AND permiso = $permiso;";
            comando.Parameters.AddWithValue("$usuario", usuarioId);
            comando.Parameters.AddWithValue("$permiso", permiso);
            var valor = await comando.ExecuteScalarAsync(cancellationToken);
            return valor is null ? null : Convert.ToInt64(valor) == 1;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<string>> ListarConcedidosAsync(ISesionPersistencia sesion, string usuarioId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT permiso FROM permiso_usuario WHERE usuario_id = $usuario AND concedido = 1 ORDER BY permiso;";
            comando.Parameters.AddWithValue("$usuario", usuarioId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<string>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(lector.GetString(0));
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
