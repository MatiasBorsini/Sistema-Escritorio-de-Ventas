using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioClientes(IGeneradorIds ids) : IRepositorioClientes
{
    public async Task<string> CrearAsync(ISesionPersistencia sesion, NuevoCliente datos, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion); ExigirEscritura(s);
        var id = ids.NuevoId();
        using var cmd = CrearComando(s, "INSERT INTO cliente (id,nombre_completo,telefono,observacion,activo,creado_en,actualizado_en) VALUES ($id,$nombre,$telefono,$observacion,1,$fecha,$fecha);");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$nombre", datos.NombreCompleto);
        cmd.Parameters.AddWithValue("$telefono", (object?)datos.Telefono ?? DBNull.Value); cmd.Parameters.AddWithValue("$observacion", (object?)datos.Observacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fecha", fecha); await cmd.ExecuteNonQueryAsync(cancellationToken); return id;
    }

    public async Task<ClienteRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion); using var cmd = CrearComando(s, $"{SelectCliente} WHERE id=$id;"); cmd.Parameters.AddWithValue("$id", id);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken); return await r.ReadAsync(cancellationToken) ? LeerCliente(r) : null;
    }

    public async Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionCliente datos, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion); ExigirEscritura(s);
        using var cmd = CrearComando(s, "UPDATE cliente SET nombre_completo=$nombre,telefono=$telefono,observacion=$observacion,actualizado_en=$fecha WHERE id=$id AND activo=1;");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$nombre", datos.NombreCompleto);
        cmd.Parameters.AddWithValue("$telefono", (object?)datos.Telefono ?? DBNull.Value); cmd.Parameters.AddWithValue("$observacion", (object?)datos.Observacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fecha", fecha);
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("El cliente no existe o está inactivo.");
    }

    public async Task CambiarActivoAsync(ISesionPersistencia sesion, string id, bool activo, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion); ExigirEscritura(s);
        using var cmd = CrearComando(s, "UPDATE cliente SET activo=$activo,actualizado_en=$fecha WHERE id=$id;");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$activo", activo ? 1 : 0); cmd.Parameters.AddWithValue("$fecha", fecha);
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("El cliente no existe.");
    }

    public async Task ConfigurarLimiteCreditoAsync(ISesionPersistencia sesion, string id, long? limiteMinor, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion); ExigirEscritura(s);
        using var cmd = CrearComando(s, "UPDATE cliente SET limite_credito_minor=$limite,actualizado_en=$fecha WHERE id=$id;");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$limite", (object?)limiteMinor ?? DBNull.Value); cmd.Parameters.AddWithValue("$fecha", fecha);
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("El cliente no existe.");
    }

    public async Task<IReadOnlyList<ClienteRegistrado>> BuscarAsync(ISesionPersistencia sesion, string texto, bool incluirInactivos, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        ValidarPagina(limite, desplazamiento); var s = Sesion(sesion); var filtro = texto.Trim();
        var sql = $"{SelectCliente} WHERE ($incluir=1 OR activo=1) AND ($filtro='' OR nombre_completo LIKE $patron COLLATE NOCASE OR telefono LIKE $patron COLLATE NOCASE) ORDER BY nombre_completo COLLATE NOCASE,id LIMIT $limite OFFSET $offset;";
        using var cmd = CrearComando(s, sql); cmd.Parameters.AddWithValue("$incluir", incluirInactivos ? 1 : 0);
        cmd.Parameters.AddWithValue("$filtro", filtro); cmd.Parameters.AddWithValue("$patron", filtro + "%"); cmd.Parameters.AddWithValue("$limite", limite + 1); cmd.Parameters.AddWithValue("$offset", desplazamiento);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken); var rows = new List<ClienteRegistrado>();
        while (await r.ReadAsync(cancellationToken)) rows.Add(LeerCliente(r)); return rows;
    }

    public async Task<IReadOnlyList<SugerenciaCliente>> BuscarSugerenciasAsync(ISesionPersistencia sesion, string texto, int limite, CancellationToken cancellationToken = default)
    {
        if (limite is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(limite));
        var s = Sesion(sesion); var filtro = texto.Trim();
        using var cmd = CrearComando(s, "SELECT id,nombre_completo,telefono FROM cliente WHERE activo=1 AND ($filtro='' OR nombre_completo LIKE $patron COLLATE NOCASE OR telefono LIKE $patron COLLATE NOCASE) ORDER BY CASE WHEN nombre_completo LIKE $patron COLLATE NOCASE THEN 0 ELSE 1 END,nombre_completo COLLATE NOCASE,id LIMIT $limite;");
        cmd.Parameters.AddWithValue("$filtro", filtro); cmd.Parameters.AddWithValue("$patron", filtro + "%"); cmd.Parameters.AddWithValue("$limite", limite);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken); var rows = new List<SugerenciaCliente>();
        while (await r.ReadAsync(cancellationToken)) rows.Add(new(r.GetString(0), r.GetString(1), NullableString(r, 2))); return rows;
    }

    public async Task<IReadOnlyList<MovimientoHistorialCliente>> ListarHistorialAsync(ISesionPersistencia sesion, string clienteId, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        ValidarPagina(limite, desplazamiento); var s = Sesion(sesion);
        using var cmd = CrearComando(s, """
            SELECT tipo,fecha,referencia,venta_referencia,estado,total_minor,diferencia_minor FROM (
                SELECT 'venta' AS tipo,v.creada_en AS fecha,v.numero AS referencia,NULL AS venta_referencia,v.estado AS estado,v.total_minor AS total_minor,NULL AS diferencia_minor
                FROM venta v WHERE v.cliente_id=$cliente
                UNION ALL
                SELECT 'cambio',c.creado_en,c.numero,v.numero,'registrado',NULL,c.diferencia_minor
                FROM cambio c JOIN venta v ON v.id=c.venta_id WHERE v.cliente_id=$cliente
                UNION ALL
                SELECT m.tipo,m.creado_en,COALESCE(v.numero,c.numero,a.numero,'Ajuste de deuda'),NULL,m.tipo,ABS(m.importe_minor),m.importe_minor
                FROM cuenta_corriente_movimiento m LEFT JOIN venta v ON v.id=m.venta_id LEFT JOIN cambio c ON c.id=m.cambio_id LEFT JOIN cuenta_corriente_abono a ON a.id=m.abono_id
                WHERE m.cliente_id=$cliente
            ) ORDER BY fecha DESC,referencia DESC LIMIT $limite OFFSET $offset;
            """);
        cmd.Parameters.AddWithValue("$cliente", clienteId); cmd.Parameters.AddWithValue("$limite", limite + 1); cmd.Parameters.AddWithValue("$offset", desplazamiento);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken); var rows = new List<MovimientoHistorialCliente>();
        while (await r.ReadAsync(cancellationToken)) rows.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), NullableString(r, 3), r.GetString(4), NullableLong(r, 5), NullableLong(r, 6)));
        return rows;
    }

    private const string SelectCliente = "SELECT id,nombre_completo,telefono,observacion,activo,creado_en,actualizado_en,limite_credito_minor FROM cliente";
    private static ClienteRegistrado LeerCliente(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), NullableString(r, 2), NullableString(r, 3), r.GetInt64(4) == 1, r.GetString(5), r.GetString(6), NullableLong(r, 7));
    private static string? NullableString(SqliteDataReader r, int index) => r.IsDBNull(index) ? null : r.GetString(index);
    private static long? NullableLong(SqliteDataReader r, int index) => r.IsDBNull(index) ? null : r.GetInt64(index);
    private static void ValidarPagina(int limite, int desplazamiento)
    {
        if (limite is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limite), "El límite debe estar entre 1 y 100.");
        if (desplazamiento < 0) throw new ArgumentOutOfRangeException(nameof(desplazamiento));
    }
    private static SqliteCommand CrearComando(ISesionSqlite s, string sql) { var cmd = s.Conexion.CreateCommand(); cmd.CommandTimeout = s.TimeoutSegundos; cmd.CommandText = sql; return cmd; }
    private static ISesionSqlite Sesion(ISesionPersistencia sesion) => sesion as ISesionSqlite ?? throw new ArgumentException("La sesión no pertenece a este proveedor SQLite.");
    private static void ExigirEscritura(ISesionSqlite s) { if (s.SoloLectura) throw new InvalidOperationException("La sesión es de solo lectura y no admite escrituras."); }
}
