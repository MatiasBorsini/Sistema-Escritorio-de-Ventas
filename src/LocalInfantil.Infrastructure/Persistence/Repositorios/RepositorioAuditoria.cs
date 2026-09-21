using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

/// <summary>
/// Registro mínimo de auditoría. Comparte la sesión (conexión y transacción)
/// de la operación que documenta: ambas se confirman o revierten juntas.
/// </summary>
public sealed class RepositorioAuditoria : IRepositorioAuditoria
{
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public RepositorioAuditoria(IGeneradorIds ids, IReloj reloj)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
    }

    public async Task<string> RegistrarAsync(ISesionPersistencia sesion, NuevoEventoAuditoria evento, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO auditoria (id, operacion_id, tipo_evento, actor_tipo, actor_id, referencia_tipo, referencia_id, detalle, creado_en) VALUES ($id, $operacion, $tipo, $actorTipo, $actor, $refTipo, $refId, $detalle, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$operacion", evento.OperacionId);
            comando.Parameters.AddWithValue("$tipo", evento.TipoEvento);
            comando.Parameters.AddWithValue("$actorTipo", evento.ActorTipo);
            comando.Parameters.AddWithValue("$actor", (object?)evento.ActorId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$refTipo", (object?)evento.ReferenciaTipo ?? DBNull.Value);
            comando.Parameters.AddWithValue("$refId", (object?)evento.ReferenciaId ?? DBNull.Value);
            comando.Parameters.AddWithValue("$detalle", (object?)evento.Detalle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$creadoEn", _reloj.UtcNow().ToString("o"));
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<EventoAuditado>> ObtenerPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, operacion_id, tipo_evento, actor_tipo, actor_id, referencia_tipo, referencia_id, detalle, creado_en FROM auditoria WHERE operacion_id = $operacion ORDER BY creado_en, id;";
            comando.Parameters.AddWithValue("$operacion", operacionId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<EventoAuditado>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new EventoAuditado(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.IsDBNull(7) ? null : lector.GetString(7),
                    lector.GetString(8)));
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
