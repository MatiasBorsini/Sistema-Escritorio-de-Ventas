using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

/// <summary>Proyección agregada de las variantes de un producto, sin consultas por variante.</summary>
public sealed class RepositorioEtiquetas : IRepositorioEtiquetas
{
    private const string Proyeccion = """
        WITH fisico AS (SELECT variante_id, SUM(cantidad) AS cantidad FROM movimiento_inventario GROUP BY variante_id),
        apartado AS (SELECT variante_id, SUM(CASE WHEN tipo = 'apartar' THEN cantidad ELSE -cantidad END) AS cantidad FROM apartado_movimiento GROUP BY variante_id)
        SELECT v.id, v.codigo_interno, p.nombre, v.talle, v.color, COALESCE(f.cantidad, 0) - COALESCE(a.cantidad, 0), pr.importe_minor, pr.moneda
        FROM variante v JOIN producto p ON p.id = v.producto_id
        LEFT JOIN fisico f ON f.variante_id = v.id LEFT JOIN apartado a ON a.variante_id = v.id
        LEFT JOIN precio pr ON pr.variante_id = v.id AND pr.vigente_desde <= $fecha AND (pr.vigente_hasta IS NULL OR $fecha < pr.vigente_hasta)
        """;

    public async Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, string fechaUtc, CancellationToken cancellationToken = default)
    {
        if (sesion is not ISesionSqlite s) throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = $"{Proyeccion} WHERE v.producto_id = $producto ORDER BY v.codigo_interno;";
            comando.Parameters.AddWithValue("$producto", productoId);
            comando.Parameters.AddWithValue("$fecha", fechaUtc);
            return await LeerAsync(comando, cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    public async Task<IReadOnlyList<VarianteParaEtiqueta>> ListarPorIdsAsync(ISesionPersistencia sesion, IReadOnlyList<string> varianteIds, string fechaUtc, CancellationToken cancellationToken = default)
    {
        if (sesion is not ISesionSqlite s) throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.", nameof(sesion));
        if (varianteIds.Count == 0) return Array.Empty<VarianteParaEtiqueta>();
        if (varianteIds.Count > 900) throw new ArgumentOutOfRangeException(nameof(varianteIds), "La selección excede el límite de lectura por lote.");
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            var parametros = new List<string>(varianteIds.Count);
            for (var i = 0; i < varianteIds.Count; i++)
            {
                var nombre = $"$id{i}"; parametros.Add(nombre); comando.Parameters.AddWithValue(nombre, varianteIds[i]);
            }
            comando.Parameters.AddWithValue("$fecha", fechaUtc);
            comando.CommandText = $"{Proyeccion} WHERE v.id IN ({string.Join(",", parametros)}) ORDER BY v.codigo_interno;";
            return await LeerAsync(comando, cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex)) { throw ErroresSqlite.OcupadoException(ex); }
    }

    private static async Task<IReadOnlyList<VarianteParaEtiqueta>> LeerAsync(SqliteCommand comando, CancellationToken ct)
    {
        await using var lector = await comando.ExecuteReaderAsync(ct);
        var filas = new List<VarianteParaEtiqueta>();
        while (await lector.ReadAsync(ct))
            filas.Add(new VarianteParaEtiqueta(lector.GetString(0), lector.GetString(1), lector.GetString(2), lector.IsDBNull(3) ? null : lector.GetString(3), lector.IsDBNull(4) ? null : lector.GetString(4), lector.GetInt64(5), lector.IsDBNull(6) ? null : lector.GetInt64(6), lector.IsDBNull(7) ? null : lector.GetString(7)));
        return filas;
    }
}
