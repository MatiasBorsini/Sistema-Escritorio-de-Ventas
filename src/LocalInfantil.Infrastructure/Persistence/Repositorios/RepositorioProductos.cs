using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioProductos : IRepositorioProductos
{
    private readonly IGeneradorIds _ids;

    public RepositorioProductos(IGeneradorIds ids)
    {
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public async Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string codigoInterno, string creadoEn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        var id = _ids.NuevoId();
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "INSERT INTO producto (id, codigo_interno, nombre, marca, categoria_id, creado_en) VALUES ($id, $codigo, $nombre, $marca, $categoriaId, $creadoEn);";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$codigo", codigoInterno);
            comando.Parameters.AddWithValue("$nombre", datos.Nombre);
            comando.Parameters.AddWithValue("$marca", (object?)datos.Marca ?? DBNull.Value);
            comando.Parameters.AddWithValue("$categoriaId", (object?)datos.CategoriaId ?? DBNull.Value);
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
    /// interno provisorio único y usa OTR como categoría de andamiaje (el
    /// esquema exige categoria_id NOT NULL). El camino de dominio
    /// (CrearProducto) siempre exige categoría explícita y genera el código
    /// jerárquico real por categoría.
    /// </summary>
    public Task<string> InsertarAsync(ISesionPersistencia sesion, NuevoProducto datos, string creadoEn, CancellationToken cancellationToken = default)
        => InsertarAsync(sesion, datos with { CategoriaId = datos.CategoriaId ?? "cat-otr" }, $"TMP-{_ids.NuevoId()[..8].ToUpperInvariant()}", creadoEn, cancellationToken);

    public async Task<ProductoRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id WHERE p.id = $id;";
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

    public async Task<ProductoRegistrado?> ObtenerPorCodigoInternoAsync(ISesionPersistencia sesion, string codigoInterno, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id WHERE p.codigo_interno = $codigo;";
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

    public async Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionProducto edicion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        var s = Sesion(sesion);
        ExigirEscritura(s);
        if (string.IsNullOrWhiteSpace(edicion.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(edicion));
        }

        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = "UPDATE producto SET nombre = $nombre, marca = $marca, categoria_id = $categoriaId WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            comando.Parameters.AddWithValue("$nombre", edicion.Nombre);
            comando.Parameters.AddWithValue("$marca", (object?)edicion.Marca ?? DBNull.Value);
            comando.Parameters.AddWithValue("$categoriaId", (object?)edicion.CategoriaId ?? DBNull.Value);
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
            comando.CommandText = "UPDATE producto SET activo = 0 WHERE id = $id;";
            comando.Parameters.AddWithValue("$id", id);
            await comando.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<ProductoRegistrado>> ListarAsync(ISesionPersistencia sesion, bool soloActivos, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = soloActivos
                ? "SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id WHERE p.activo = 1 ORDER BY p.nombre;"
                : "SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id ORDER BY p.nombre;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ProductoRegistrado>();
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

    public async Task<IReadOnlyList<ProductoRegistrado>> BuscarAsync(ISesionPersistencia sesion, string texto, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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
            comando.CommandText = "SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id WHERE p.nombre LIKE $t OR p.marca LIKE $t OR c.nombre LIKE $t OR p.codigo_interno LIKE $t ORDER BY p.nombre, p.id LIMIT $limite OFFSET $desplazamiento;";
            comando.Parameters.AddWithValue("$t", $"%{texto.Trim()}%");
            comando.Parameters.AddWithValue("$limite", limite);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ProductoRegistrado>();
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

    public async Task<IReadOnlyList<ResumenProducto>> BuscarResumenAsync(ISesionPersistencia sesion, string texto, int limite, int desplazamiento, CancellationToken cancellationToken = default)
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
            // Una sola lectura: mismo filtro y orden que BuscarAsync, con
            // agregados por producto (COUNT de variantes, SUM de existencias
            // por variante) y una fila extra de peek para HaySiguiente.
            comando.CommandText = """
                SELECT p.id, p.codigo_interno, p.nombre, p.marca, c.nombre, p.categoria_id, p.activo,
                       COUNT(v.id),
                       COALESCE(SUM(COALESCE((SELECT SUM(m.cantidad) FROM movimiento_inventario m WHERE m.variante_id = v.id), 0)), 0)
                FROM producto p LEFT JOIN categoria c ON c.id = p.categoria_id LEFT JOIN variante v ON v.producto_id = p.id
                WHERE p.nombre LIKE $t OR p.marca LIKE $t OR c.nombre LIKE $t OR p.codigo_interno LIKE $t
                GROUP BY p.id ORDER BY p.nombre, p.id LIMIT $limite OFFSET $desplazamiento;
                """;
            comando.Parameters.AddWithValue("$t", $"%{texto.Trim()}%");
            comando.Parameters.AddWithValue("$limite", limite + 1);
            comando.Parameters.AddWithValue("$desplazamiento", desplazamiento);
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<ResumenProducto>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new ResumenProducto(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.IsDBNull(3) ? null : lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.GetInt64(6) == 1,
                    (int)lector.GetInt64(7),
                    lector.GetInt64(8)));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<IReadOnlyList<CategoriaRegistrada>> ListarCategoriasAsync(ISesionPersistencia sesion, bool soloActivas, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        try
        {
            using var comando = s.Conexion.CreateCommand();
            comando.CommandTimeout = s.TimeoutSegundos;
            comando.CommandText = soloActivas
                ? "SELECT id, nombre, prefijo, orden, activa FROM categoria WHERE activa = 1 ORDER BY orden, nombre;"
                : "SELECT id, nombre, prefijo, orden, activa FROM categoria ORDER BY orden, nombre;";
            await using var lector = await comando.ExecuteReaderAsync(cancellationToken);
            var resultado = new List<CategoriaRegistrada>();
            while (await lector.ReadAsync(cancellationToken))
            {
                resultado.Add(new CategoriaRegistrada(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetInt32(3),
                    lector.GetInt64(4) == 1));
            }

            return resultado;
        }
        catch (SqliteException ex) when (ErroresSqlite.EsBloqueo(ex))
        {
            throw ErroresSqlite.OcupadoException(ex);
        }
    }

    public async Task<int> ObtenerSiguienteNumeroProductoAsync(ISesionPersistencia sesion, string categoriaId, CancellationToken cancellationToken = default)
    {
        var s = Sesion(sesion);
        ExigirEscritura(s);
        // Sin transacción anidada: la sesión ya abrió BEGIN IMMEDIATE, por lo
        // que SELECT+UPSERT corren dentro de la transacción del alta.
        try
        {
            int numero;
            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "SELECT ultimo_numero FROM secuencia_producto WHERE categoria_id = $cat;";
                cmd.Parameters.AddWithValue("$cat", categoriaId);
                var result = await cmd.ExecuteScalarAsync(cancellationToken);
                numero = result is null || result is DBNull ? 1 : Convert.ToInt32(result) + 1;
            }

            using (var cmd = s.Conexion.CreateCommand())
            {
                cmd.CommandTimeout = s.TimeoutSegundos;
                cmd.CommandText = "INSERT INTO secuencia_producto (categoria_id, ultimo_numero) VALUES ($cat, $num) ON CONFLICT(categoria_id) DO UPDATE SET ultimo_numero = $num;";
                cmd.Parameters.AddWithValue("$cat", categoriaId);
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

    private static ProductoRegistrado Leer(SqliteDataReader lector)
    {
        return new ProductoRegistrado(
            lector.GetString(0),
            lector.GetString(1),
            lector.GetString(2),
            lector.IsDBNull(3) ? null : lector.GetString(3),
            lector.IsDBNull(4) ? null : lector.GetString(4),
            lector.IsDBNull(5) ? null : lector.GetString(5),
            lector.GetInt64(6) == 1);
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