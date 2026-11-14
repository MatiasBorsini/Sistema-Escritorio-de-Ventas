using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioPagos : IRepositorioPagos
{
    private readonly IGeneradorIds _ids;

    public RepositorioPagos(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(
        ISesionPersistencia sesion,
        string ventaId,
        string medio,
        long importeMinor,
        long? recibidoMinor,
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
            comando.CommandText = "INSERT INTO pago (id, venta_id, medio, importe_minor, recibido_minor, creado_en) VALUES ($id, $venta, $medio, $importe, $recibido, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$venta", ventaId);
            comando.Parameters.AddWithValue("$medio", medio);
            comando.Parameters.AddWithValue("$importe", importeMinor);
            comando.Parameters.AddWithValue("$recibido", (object?)recibidoMinor ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<PagoRegistrado>> ListarPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, medio, importe_minor, recibido_minor FROM pago WHERE venta_id = $venta ORDER BY medio, id;";
            comando.Parameters.AddWithValue("$venta", ventaId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<PagoRegistrado>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new PagoRegistrado(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetInt64(2),
                    lector.IsDBNull(3) ? null : lector.GetInt64(3)));
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
