using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioVariantes : IRepositorioVariantes
{
    private readonly IGeneradorIds _ids;

    public RepositorioVariantes(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string codigoInterno, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            // Compat: la columna heredada sku (V1, NOT NULL) espeja el código
            // interno. Una sola convención; sin segundo formato para typeahead.
            // talle_norm/color_norm usan EXACTAMENTE la normalización central
            // de Application (misma función que valida el caso de uso).
            comando.CommandText = "INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, color, talle_norm, color_norm, creado_en) VALUES ($id, $producto, $codigo, $codigo, $talle, $color, $talleNorm, $colorNorm, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$producto", datos.ProductoId);
            comando.Parameters.AddWithValue("$codigo", codigoInterno);
            comando.Parameters.AddWithValue("$talle", (object?)datos.Talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)datos.Color ?? DBNull.Value);
            comando.Parameters.AddWithValue("$talleNorm", NormalizacionVariante.NormalizarAtributo(datos.Talle));
            comando.Parameters.AddWithValue("$colorNorm", NormalizacionVariante.NormalizarAtributo(datos.Color));
            comando.Parameters.AddWithValue("$creadoEn", creadoEn);
            await comando.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    /// <summary>
    /// Soporte para pruebas de persistencia de bajo nivel: genera un código
    /// interno provisorio único. El camino de dominio (CrearVariante) siempre
    /// genera el código jerárquico real {CODIGO_PRODUCTO}-{NN}.
    /// </summary>
    public Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string creadoEn, CancellationToken cancellationToken = default)
        => InsertarAsync(sesion, datos, $"TMP-{_ids.NuevoId()[..8].ToUpperInvariant()}", creadoEn, cancellationToken);

    public async Task<VarianteRegistrada?> ObtenerPorCodigoInternoAsync(ISesionPersistencia sesion, string codigoInterno, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, codigo_interno, talle, color, activa FROM variante WHERE codigo_interno = $codigo OR sku = $codigo;";
            comando.Parameters.AddWithValue("$codigo", codigoInterno);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return Leer(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<VarianteRegistrada?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, codigo_interno, talle, color, activa FROM variante WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            if (!await lector.ReadAsync(cancellationToken))
            {
                return null;
            }

            return Leer(lector);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionVariante edicion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE variante SET talle = $talle, color = $color, talle_norm = $talleNorm, color_norm = $colorNorm WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$talle", (object?)edicion.Talle ?? DBNull.Value);
            comando.Parameters.AddWithValue("$color", (object?)edicion.Color ?? DBNull.Value);
            comando.Parameters.AddWithValue("$talleNorm", NormalizacionVariante.NormalizarAtributo(edicion.Talle));
            comando.Parameters.AddWithValue("$colorNorm", NormalizacionVariante.NormalizarAtributo(edicion.Color));
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task DesactivarAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE variante SET activa = 0 WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<VarianteRegistrada>> ListarPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT id, producto_id, codigo_interno, talle, color, activa FROM variante WHERE producto_id = $producto ORDER BY codigo_interno;";
            comando.Parameters.AddWithValue("$producto", productoId);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<VarianteRegistrada>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(Leer(lector));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<long> ContarActivasPorProductoAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COUNT(*) FROM variante WHERE producto_id = $producto AND activa = 1;";
            comando.Parameters.AddWithValue("$producto", productoId);
            return Convert.ToInt64(await comando.ExecuteScalarAsync(cancellationToken));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<SugerenciaVariante>> BuscarSugerenciasAsync(ISesionPersistencia sesion, string texto, int limite, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        if (limite < 1 || limite > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limite), "El límite debe estar entre 1 y 100.");
        }

        var t = texto.Trim();
        if (t.Length == 0)
        {
            return Array.Empty<SugerenciaVariante>();
        }

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.producto_id, v.codigo_interno, p.nombre, p.marca, c.nombre, v.talle, v.color, v.activa
                FROM variante v JOIN producto p ON p.id = v.producto_id LEFT JOIN categoria c ON c.id = p.categoria_id
                WHERE v.codigo_interno LIKE $contiene OR v.sku LIKE $contiene OR p.nombre LIKE $contiene OR p.codigo_interno LIKE $contiene
                ORDER BY CASE WHEN v.codigo_interno = $exacto THEN 0 WHEN v.codigo_interno LIKE $prefijo THEN 1 WHEN p.nombre LIKE $prefijo THEN 2 ELSE 3 END, v.codigo_interno
                LIMIT $limite;
                """;
            comando.Parameters.AddWithValue("$contiene", $"%{t}%");
            comando.Parameters.AddWithValue("$exacto", t);
            comando.Parameters.AddWithValue("$prefijo", $"{t}%");
            comando.Parameters.AddWithValue("$limite", limite);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<SugerenciaVariante>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new SugerenciaVariante(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.IsDBNull(7) ? null : lector.GetString(7),
                    lector.GetInt64(8) == 1));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<FichaVariante>> ObtenerFichaPorProductoAsync(ISesionPersistencia sesion, string productoId, string fecha, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = """
                SELECT v.id, v.codigo_interno, v.talle, v.color, v.activa,
                    (SELECT p.importe_minor FROM precio p WHERE p.variante_id = v.id AND p.vigente_desde <= $fecha AND (p.vigente_hasta IS NULL OR $fecha < p.vigente_hasta) ORDER BY p.vigente_desde DESC LIMIT 1),
                    (SELECT p.moneda FROM precio p WHERE p.variante_id = v.id AND p.vigente_desde <= $fecha AND (p.vigente_hasta IS NULL OR $fecha < p.vigente_hasta) ORDER BY p.vigente_desde DESC LIMIT 1),
                    COALESCE((SELECT SUM(m.cantidad) FROM movimiento_inventario m WHERE m.variante_id = v.id), 0)
                FROM variante v WHERE v.producto_id = $producto ORDER BY v.codigo_interno;
                """;
            comando.Parameters.AddWithValue("$producto", productoId);
            comando.Parameters.AddWithValue("$fecha", fecha);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<FichaVariante>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new FichaVariante(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.IsDBNull(2) ? null : lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.GetInt64(4) == 1,
                    lector.IsDBNull(5) ? null : lector.GetInt64(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.GetInt64(7)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<int> ObtenerSiguienteNumeroVarianteAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        // Sin transacción anidada: corre dentro del BEGIN IMMEDIATE del alta.
        try
        {
            int numero;
            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "SELECT ultimo_numero FROM secuencia_variante WHERE producto_id = $prod;";
                cmd.Parameters.AddWithValue("$prod", productoId);
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                numero = result is null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
            }

            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "INSERT INTO secuencia_variante (producto_id, ultimo_numero) VALUES ($prod, $num) ON CONFLICT(producto_id) DO UPDATE SET ultimo_numero = $num;";
                cmd.Parameters.AddWithValue("$prod", productoId);
                cmd.Parameters.AddWithValue("$num", numero);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            return numero;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<int> ObtenerUltimoNumeroVarianteAsync(ISesionPersistencia sesion, string productoId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT COALESCE((SELECT ultimo_numero FROM secuencia_variante WHERE producto_id = $producto), 0);";
            comando.Parameters.AddWithValue("$producto", productoId);
            return Convert.ToInt32(await comando.ExecuteScalarAsync(cancellationToken));
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    private static VarianteRegistrada Leer(SqliteDataReader lector)
    {
        return new VarianteRegistrada(
            lector.GetString(0),
            lector.GetString(1),
            lector.GetString(2),
            lector.IsDBNull(3) ? null : lector.GetString(3),
            lector.IsDBNull(4) ? null : lector.GetString(4),
            lector.GetInt64(5) == 1);
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
