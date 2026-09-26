using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioInicio : IRepositorioInicio
{
    public async Task<ResumenInventarioInicio> ObtenerInventarioAsync(ISesionPersistencia sesion, CancellationToken ct = default)
    {
        var s = Sesion(sesion); using var c = s.Conexion.CreateCommand(); c.CommandTimeout = s.TimeoutSegundos;
        c.CommandText = """SELECT COALESCE((SELECT SUM(cantidad) FROM movimiento_inventario),0), COALESCE((SELECT SUM(CASE WHEN tipo='apartar' THEN cantidad ELSE -cantidad END) FROM apartado_movimiento),0);""";
        await using var r = await c.ExecuteReaderAsync(ct); await r.ReadAsync(ct);
        var fisico = r.GetInt64(0); var apartado = r.GetInt64(1); return new(fisico, fisico - apartado, apartado);
    }
    public async Task<int> ContarConteosBorradorAsync(ISesionPersistencia sesion, CancellationToken ct = default)
    {
        var s = Sesion(sesion); using var c = s.Conexion.CreateCommand(); c.CommandTimeout = s.TimeoutSegundos; c.CommandText = "SELECT COUNT(*) FROM conteo WHERE estado = 'borrador';";
        return Convert.ToInt32(await c.ExecuteScalarAsync(ct));
    }
    public async Task<IReadOnlyList<EventoInicioOrigen>> ListarActividadAsync(ISesionPersistencia sesion, int limite, CancellationToken ct = default)
    {
        if (limite is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(limite));
        var s = Sesion(sesion); using var c = s.Conexion.CreateCommand(); c.CommandTimeout = s.TimeoutSegundos; c.Parameters.AddWithValue("$l", limite);
        c.CommandText = """
            SELECT a.tipo_evento, COALESCE(p.nombre, v.sku),
                   CASE WHEN a.tipo_evento LIKE 'compra.recepcion%' THEN (SELECT SUM(l.cantidad) FROM recepcion_linea l WHERE l.recepcion_id=r.id) ELSE am.cantidad END,
                   a.detalle, u.nombre, a.creado_en
            FROM auditoria a LEFT JOIN usuario u ON u.id=a.actor_id
            LEFT JOIN recepcion r ON a.referencia_tipo='recepcion' AND r.id=a.referencia_id
            LEFT JOIN proveedor p ON p.id=r.proveedor_id
            LEFT JOIN variante v ON a.referencia_tipo='variante' AND v.id=a.referencia_id
            LEFT JOIN apartado_movimiento am ON am.operacion_id=a.operacion_id AND am.variante_id=a.referencia_id
            WHERE a.tipo_evento IN ('compra.recepcion_confirmada','compra.recepcion_revertida','inventario.conteo_creado','inventario.conteo_corregido','inventario.conteo_aprobado','inventario.apartado','inventario.reincorporado','inventario.baja_definitiva')
            ORDER BY a.creado_en DESC, a.id DESC LIMIT $l;
            """;
        var resultado = new List<EventoInicioOrigen>(); await using var r = await c.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) resultado.Add(new(r.GetString(0), r.IsDBNull(1)?null:r.GetString(1), r.IsDBNull(2)?null:r.GetInt64(2), r.IsDBNull(3)?null:r.GetString(3), r.IsDBNull(4)?null:r.GetString(4), r.GetString(5)));
        return resultado;
    }
    private static ISesionSqlite Sesion(ISesionPersistencia s) => s as ISesionSqlite ?? throw new ArgumentException("La sesión no pertenece a SQLite.");
}
