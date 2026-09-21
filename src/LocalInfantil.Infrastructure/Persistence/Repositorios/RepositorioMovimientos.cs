using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioMovimientos : IRepositorioMovimientos
{
    private readonly IGeneradorIds _ids;

    public RepositorioMovimientos(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> RegistrarAsync(ISesionPersistencia sesion, NuevoMovimiento datos, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, motivo, operacion_id, creado_por_usuario_id, creado_en) VALUES ($id, $variante, $tipo, $cantidad, $motivo, $operacion, $usuario, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$variante", datos.VarianteId);
            comando.Parameters.AddWithValue("$tipo", datos.Tipo);
            comando.Parameters.AddWithValue("$cantidad", datos.Cantidad);
            comando.Parameters.AddWithValue("$motivo", (object?)datos.Motivo ?? DBNull.Value);
            comando.Parameters.AddWithValue("$operacion", datos.OperacionId);
            comando.Parameters.AddWithValue("$usuario", (object?)datos.CreadoPorUsuarioId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<long> SumarPorVarianteAsync(ISesionPersistencia sesion, string varianteId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COALESCE(SUM(cantidad), 0) FROM movimiento_inventario WHERE variante_id = $variante;";
            comando.Parameters.AddWithValue("$variante", varianteId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken));
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
