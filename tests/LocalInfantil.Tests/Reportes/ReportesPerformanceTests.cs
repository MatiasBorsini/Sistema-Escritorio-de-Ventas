using System.Diagnostics;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// F09-T05: fixture temporal representativa y medición de consultas
/// frecuentes. Sin índices "por intuición": se mide primero y solo se agrega
/// lo que la medición justifique. Seeds con SQL directo (rapidez); las reglas
/// de negocio ya están cubiertas por los tests funcionales.
/// </summary>
public sealed class ReportesPerformanceTests
{
    private readonly ITestOutputHelper _salida;

    public ReportesPerformanceTests(ITestOutputHelper salida)
    {
        _salida = salida;
    }

    /// <summary>
    /// Techo generoso: el objetivo orientativo es &lt;2 s por consulta
    /// frecuente; 15 s absorbe máquinas lentas sin ocultar regresiones
    /// gruesas (las consultas medidas están en decenas de ms).
    /// </summary>
    private const long TechoMs = 15000;

    [Fact]
    public async Task Consultas_frecuentes_responden_en_tiempo_interactivo()
    {
        var (dir, ruta) = CrearBaseTemporal();
        try
        {
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var semilla = SembrarFixtureGrande(ruta);
            _salida.WriteLine($"Fixture: {semilla}");

            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var repo = new RepositorioReportes();
            await using var sesion = await fabrica.IniciarLecturaAsync();
            var (d, h) = ("2026-09-01T00:00:00.0000000+00:00", "2026-10-01T00:00:00.0000000+00:00");
            var (dm, hm) = ("2026-10-01T00:00:00.0000000+00:00", "2026-11-01T00:00:00.0000000+00:00");

            await MedirAsync("ventas-hoy", async () => (object)await repo.ResumenVentasAsync(sesion, "2026-10-05T00:00:00.0000000+00:00", "2026-10-06T00:00:00.0000000+00:00", default));
            await MedirAsync("ventas-mes", async () => (object)await repo.ResumenVentasAsync(sesion, dm, hm, default));
            await MedirAsync("ventas-rango", async () => (object)await repo.ResumenVentasAsync(sesion, d, hm, default));
            await MedirAsync("ventas-por-dia", async () => (object)await repo.VentasPorDiaAsync(sesion, dm, hm, "-3 hours", default));
            await MedirAsync("detalle-ventas", async () => (object)await repo.DetalleVentasAsync(sesion, dm, hm, 20, 0, default));
            await MedirAsync("medios", async () => (object)await repo.DesgloseMediosAsync(sesion, dm, hm, default));
            await MedirAsync("stock-actual", async () => (object)await repo.StockActualAsync(sesion, string.Empty, null, false, null, 20, 0, default));
            await MedirAsync("stock-texto", async () => (object)await repo.StockActualAsync(sesion, "Prenda", null, false, null, 20, 0, default));
            await MedirAsync("ranking", async () => (object)await repo.RankingVentasAsync(sesion, dm, hm, 20, 0, default));
            await MedirAsync("ranking-producto", async () => (object)await repo.RankingPorProductoAsync(sesion, dm, hm, 20, 0, default));
            await MedirAsync("sin-ventas", async () => (object)await repo.SinVentasAsync(sesion, "2026-10-05T00:00:00.0000000+00:00", 20, 0, default));
            var repoCaja = new RepositorioCaja(new GeneradorIds());
            await MedirAsync("caja-historial", async () => (object)await repoCaja.ListarSesionesPorFechaAsync(sesion, dm, hm, 20, 0, default));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    private async Task MedirAsync(string nombre, Func<Task<object>> consulta)
    {
        // Calentamiento fuera de medición.
        await consulta();
        var cronometro = Stopwatch.StartNew();
        var resultado = await consulta();
        cronometro.Stop();
        var filas = resultado is System.Collections.ICollection c ? c.Count : 1;
        _salida.WriteLine($"{nombre}: {cronometro.ElapsedMilliseconds} ms ({filas} filas)");
        Assert.True(cronometro.ElapsedMilliseconds < TechoMs, $"{nombre} superó el techo de {TechoMs} ms");
    }

    private static (string Directorio, string Ruta) CrearBaseTemporal()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        return (directorio, Path.Combine(directorio, "perf-f09.db"));
    }

    private static string SembrarFixtureGrande(string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false }.ToString());
        conexion.Open();
        using (var pragma = conexion.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }

        var baseFecha = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        string Fecha(int dia, int hora, int minuto = 0) => baseFecha.AddDays(dia).AddHours(hora).AddMinutes(minuto).ToString("o");
        var rnd = new Random(1234);
        var medios = new[] { "efectivo", "transferencia", "debito", "credito", "billetera" };
        var talles = new[] { "S", "M", "L", "XL" };

        using var tx = conexion.BeginTransaction();
        int n = 0;
        string Id(string prefijo) => $"{prefijo}-{System.Threading.Interlocked.Increment(ref n):D6}";

        var paso = 0;
        void Ejecutar(string sql, params (string, object)[] parametros)
        {
            paso++;
            try
            {
                using var cmd = conexion.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Seed paso {paso}: {sql[..80]}", ex);
            }
        }

        // Usuario + categoría + productos/variantes/precios/stock.
        Ejecutar("INSERT INTO usuario (id, nombre, rol, activo, creado_en) VALUES ('fx-u1', 'Dueña Fx', 'duena', 1, $f)", ("$f", Fecha(0, 0)));
        Ejecutar("INSERT INTO categoria (id, nombre, prefijo, activa, orden, creado_en) VALUES ('fx-cat', 'Fx', 'FX', 1, 1, $f)", ("$f", Fecha(0, 0)));
        var variantes = new List<(string Id, string ProductoId, long Precio)>();
        for (var p = 0; p < 300; p++)
        {
            var prodId = $"fx-p-{p:D4}";
            Ejecutar("INSERT INTO producto (id, codigo_interno, nombre, marca, categoria_id, activo, creado_en) VALUES ($id, $cod, $nom, NULL, 'fx-cat', 1, $f)",
                ("$id", prodId), ("$cod", $"FX-{p:D4}"), ("$nom", $"Prenda FX {p:D4}"), ("$f", Fecha(0, 0)));
            for (var t = 0; t < 4; t++)
            {
                var varId = $"fx-v-{p:D4}-{t}";
                var talle = talles[t];
                Ejecutar("INSERT INTO variante (id, producto_id, sku, codigo_interno, talle, color, talle_norm, color_norm, activa, creado_en) VALUES ($id, $p, $sku, $sku, $t, NULL, $tn, '', 1, $f)",
                    ("$id", varId), ("$p", prodId), ("$sku", $"FX-{p:D4}-{t}"), ("$t", talle), ("$tn", talle), ("$f", Fecha(0, 0)));
                var precio = 5000 + ((p * 4 + t) % 20) * 1000;
                Ejecutar("INSERT INTO precio (id, variante_id, importe_minor, moneda, vigente_desde, vigente_hasta, creado_en) VALUES ($id, $v, $imp, 'ARS', $f, NULL, $f)",
                    ("$id", Id("fx-pr")), ("$v", varId), ("$imp", (object)precio), ("$f", Fecha(0, 0)));
                Ejecutar("INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, motivo, operacion_id, creado_en) VALUES ($id, $v, 'inicial', 50, 'Fx', $op, $f)",
                    ("$id", Id("fx-mi")), ("$v", varId), ("$op", Id("fx-op")), ("$f", Fecha(0, 0)));
                variantes.Add((varId, prodId, precio));
            }
        }

        // Clientes para fiado/abonos.
        var clientes = new List<string>();
        for (var c = 0; c < 50; c++)
        {
            var cli = $"fx-c-{c:D3}";
            Ejecutar("INSERT INTO cliente (id, nombre_completo, telefono, observacion, activo, creado_en, actualizado_en, limite_credito_minor) VALUES ($id, $nom, NULL, NULL, 1, $f, $f, 10000000)",
                ("$id", cli), ("$nom", $"Cliente FX {c:D3}"), ("$f", Fecha(0, 0)));
            clientes.Add(cli);
        }

        // Caja: 8 sesiones (7 cerradas + 1 abierta) cubriendo el bimestre.
        // Se insertan abiertas de a una y se cierran en el acto: el índice
        // parcial ux_caja_una_sesion_abierta admite una sola abierta.
        var sesiones = new List<string>();
        for (var s = 0; s < 8; s++)
        {
            var ses = $"fx-cs-{s}";
            Ejecutar("INSERT INTO caja_sesion (id, usuario_id, estado, fondo_inicial_minor, moneda, abierta_en, operacion_apertura_id) VALUES ($id, 'fx-u1', 'abierta', 20000, 'ARS', $ab, $opa)",
                ("$id", ses), ("$ab", Fecha(s * 8, 9)), ("$opa", $"fx-opa-{s}"));
            sesiones.Add(ses);
            if (s < 7)
            {
                Ejecutar("UPDATE caja_sesion SET estado = 'cerrada', cerrada_en = $ce, cerrado_por_usuario_id = 'fx-u1', operacion_cierre_id = $opc, efectivo_esperado_minor = 20000, efectivo_contado_minor = 20000, diferencia_minor = 0 WHERE id = $id",
                    ("$ce", Fecha(s * 8 + 1, 20)), ("$opc", $"fx-opc-{s}"), ("$id", ses));
            }
        }

        // Ventas: 5000 en 61 días, 3 líneas c/u, 5% anuladas, algunas fiadas.
        var nVentas = 0;
        var nLineas = 0;
        var nCambios = 0;
        var primerasLineas = new Dictionary<string, (string LineaId, string VarianteId, long Precio)>();
        var pagosParaCorregir = new List<(string PagoId, string MovId, long Importe)>();
        for (var i = 0; i < 5000; i++)
        {
            var dia = (i * 17) % 61;
            var hora = 9 + (i % 10);
            var f = Fecha(dia, hora, i % 60);
            var ventaId = $"fx-t-{i:D5}";
            var numero = $"T-{(i + 1):D6}";
            var op = $"fx-opv-{i:D5}";
            var esFiada = i % 20 == 0;
            var cli = esFiada ? clientes[(i / 20) % 50] : null;
            long total = 0;
            var lineas = new List<(string VarId, long Cant, long Precio, string Codigo)>();
            for (var l = 0; l < 3; l++)
            {
                var (varId, _, precio) = variantes[(i * 3 + l) % variantes.Count];
                var cant = 1 + ((i + l) % 2);
                total += cant * precio;
                lineas.Add((varId, cant, precio, $"FX-L-{nLineas}"));
                nLineas++;
            }

            Ejecutar("INSERT INTO venta (id, numero, operacion_id, estado, total_minor, moneda, referencia_fiscal, creada_en, confirmada_en, creado_por_usuario_id, cliente_id, cliente_nombre_snapshot) VALUES ($id, $num, $op, 'confirmada', $tot, 'ARS', NULL, $f, $f, 'fx-u1', $cli, $clin)",
                ("$id", ventaId), ("$num", numero), ("$op", op), ("$tot", total), ("$f", f),
                ("$cli", (object?)cli ?? DBNull.Value), ("$clin", (object?)(cli is null ? null : $"Cliente FX") ?? DBNull.Value));
            nVentas++;
            var primera = true;
            foreach (var (varId, cant, precio, _) in lineas)
            {
                var lineaId = Id("fx-vl");
                if (primera) { primerasLineas[ventaId] = (lineaId, varId, precio); primera = false; }
                Ejecutar("INSERT INTO venta_linea (id, venta_id, variante_id, cantidad, precio_unitario_minor, subtotal_minor, codigo_interno, nombre_producto, talle, color) VALUES ($id, $v, $var, $cant, $pre, $sub, $cod, $nom, 'M', NULL)",
                    ("$id", lineaId), ("$v", ventaId), ("$var", varId), ("$cant", cant), ("$pre", precio), ("$sub", cant * precio),
                    ("$cod", varId), ("$nom", "Fx"));
                Ejecutar("INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, motivo, operacion_id, creado_en) VALUES ($id, $var, 'egreso', $cant, 'Venta', $op, $f)",
                    ("$id", Id("fx-me")), ("$var", varId), ("$cant", -cant), ("$op", op), ("$f", f));
            }

            if (esFiada)
            {
                Ejecutar("INSERT INTO cuenta_corriente_movimiento (id, cliente_id, tipo, importe_minor, venta_id, cambio_id, abono_id, operacion_id, vencimiento, motivo, usuario_id, creado_en) VALUES ($id, $cli, 'cargo_venta', $imp, $v, NULL, NULL, $op, NULL, NULL, 'fx-u1', $f)",
                    ("$id", Id("fx-cc")), ("$cli", cli!), ("$imp", total), ("$v", ventaId), ("$op", op), ("$f", f));
            }
            else
            {
                var medio = medios[i % 5];
                var recibido = medio == "efectivo" ? (object)(total + 1000) : DBNull.Value;
                var pagoId = $"fx-pg-{i:D5}";
                Ejecutar("INSERT INTO pago (id, venta_id, medio, importe_minor, recibido_minor, creado_en) VALUES ($id, $v, $med, $imp, $rec, $f)",
                    ("$id", pagoId), ("$v", ventaId), ("$med", medio), ("$imp", total), ("$rec", recibido), ("$f", f));
            }

            // Caja: operacion + movimiento venta por pago (o nada si fiada).
            var sesIdx = 7;
            var cop = $"fx-cop-{i:D5}";
            Ejecutar("INSERT INTO caja_operacion (id, operacion_id, sesion_id, tipo, usuario_id, creado_en) VALUES ($id, $op, $s, 'venta', 'fx-u1', $f)",
                ("$id", cop), ("$op", op), ("$s", sesiones[sesIdx]), ("$f", f));
            if (!esFiada)
            {
                var medio = medios[i % 5];
                var movId = Id("fx-cm");
                Ejecutar("INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en, caja_operacion_id, pago_id, medio, categoria, usuario_id) VALUES ($id, $s, 'venta', $imp, $v, NULL, $f, $cop2, $pg, $med, NULL, 'fx-u1')",
                    ("$id", movId), ("$s", sesiones[sesIdx]), ("$imp", total), ("$v", ventaId), ("$f", f),
                    ("$cop2", cop), ("$pg", $"fx-pg-{i:D5}"), ("$med", medio));
                if (pagosParaCorregir.Count < 20 && medio == "debito")
                    pagosParaCorregir.Add(($"fx-pg-{i:D5}", movId, total));
            }

            if (i % 20 == 19)
            {
                // Anulada (5%): reversion + reintegro + restitución de stock.
                var ro = $"fx-rop-{i:D5}";
                Ejecutar("INSERT INTO reversion_venta (id, venta_id, operacion_id, motivo, creado_por_usuario_id, creado_en) VALUES ($id, $v, $op, 'Fx', 'fx-u1', $f)",
                    ("$id", Id("fx-rv")), ("$v", ventaId), ("$op", ro), ("$f", f));
                Ejecutar("UPDATE venta SET estado = 'anulada' WHERE id = $v", ("$v", ventaId));
                foreach (var (varId, cant, _, _) in lineas)
                {
                    Ejecutar("INSERT INTO movimiento_inventario (id, variante_id, tipo, cantidad, motivo, operacion_id, creado_en) VALUES ($id, $var, 'ingreso', $cant, 'Anulacion', $op, $f)",
                        ("$id", Id("fx-mi")), ("$var", varId), ("$cant", cant), ("$op", ro), ("$f", f));
                }
            }
        }

        // Correcciones de medio: 20 pagos débito -> efectivo con par compensatorio.
        foreach (var (pagoId, movId, importe) in pagosParaCorregir)
        {
            var copc = Id("fx-copc");
            var f = Fecha(40, 12);
            Ejecutar("INSERT INTO caja_operacion (id, operacion_id, sesion_id, tipo, usuario_id, creado_en) VALUES ($id, $op, $s, 'correccion_medio', 'fx-u1', $f)",
                ("$id", copc), ("$op", copc), ("$s", sesiones[7]), ("$f", f));
            Ejecutar("INSERT INTO pago_correccion (id, pago_id, medio_anterior, medio_nuevo, importe_minor, motivo, usuario_id, operacion_id, caja_operacion_id, creado_en) VALUES ($id, $pg, 'debito', 'efectivo', $imp, 'Fx', 'fx-u1', $op, $cop, $f)",
                ("$id", Id("fx-pc")), ("$pg", pagoId), ("$imp", importe), ("$op", copc), ("$cop", copc), ("$f", f));
            Ejecutar("INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en, caja_operacion_id, pago_id, medio, categoria, usuario_id, movimiento_origen_id) VALUES ($id, $s, 'correccion', $imp, NULL, 'Fx', $f, $cop, $pg, 'debito', NULL, 'fx-u1', $orig)",
                ("$id", Id("fx-cm")), ("$s", sesiones[7]), ("$imp", -importe), ("$f", f), ("$cop", copc), ("$pg", pagoId), ("$orig", movId));
            Ejecutar("INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en, caja_operacion_id, pago_id, medio, categoria, usuario_id, movimiento_origen_id) VALUES ($id, $s, 'correccion', $imp, NULL, 'Fx', $f, $cop, $pg, 'efectivo', NULL, 'fx-u1', $orig)",
                ("$id", Id("fx-cm")), ("$s", sesiones[7]), ("$imp", importe), ("$f", f), ("$cop", copc), ("$pg", pagoId), ("$orig", movId));
        }

        // Cambios: 300 sobre ventas no anuladas (la entrada usa la línea real).
        for (var k = 0; k < 300; k++)
        {
            var dia = (k * 7) % 61;
            var f = Fecha(dia, 10, k % 60);
            var cambioId = $"fx-c-{k:D4}";
            var vi = (k * 13) % 5000;
            if (vi % 20 == 19) vi = (vi + 1) % 5000;
            var ventaRef = $"fx-t-{vi:D5}";
            var (lineaRef, varEnt, precioEnt) = primerasLineas[ventaRef];
            var (varSal, _, precioSal) = variantes[(k + 5) % variantes.Count];
            // Solo hay cobro si la salida supera la entrada; si no, devolución pura.
            var salidas = k % 2 == 0 && precioSal > precioEnt ? precioSal : 0;
            var cop = $"fx-cop-c{k:D4}";
            Ejecutar("INSERT INTO cambio (id, numero, operacion_id, venta_id, usuario_id, creado_en, importe_entradas_minor, importe_salidas_minor, diferencia_minor, motivo) VALUES ($id, $num, $op, $v, 'fx-u1', $f, $ent, $sal, $dif, 'Fx')",
                ("$id", cambioId), ("$num", $"C-{(k + 1):D6}"), ("$op", cop), ("$v", ventaRef), ("$f", f),
                ("$ent", precioEnt), ("$sal", salidas), ("$dif", salidas - precioEnt));
            Ejecutar("INSERT INTO cambio_entrada (id, cambio_id, venta_linea_id, variante_id, cantidad, precio_unitario_minor, apta, motivo_no_apta, codigo_interno, nombre_producto, talle, color) VALUES ($id, $c, $vl, $var, 1, $pre, 1, NULL, $cod, 'Fx', 'M', NULL)",
                ("$id", Id("fx-ce")), ("$c", cambioId), ("$vl", lineaRef), ("$var", varEnt), ("$pre", precioEnt), ("$cod", varEnt));
            if (salidas > 0)
            {
                Ejecutar("INSERT INTO cambio_salida (id, cambio_id, variante_id, cantidad, precio_unitario_minor, codigo_interno, nombre_producto, talle, color) VALUES ($id, $c, $var, 1, $pre, $cod, 'Fx', 'M', NULL)",
                    ("$id", Id("fx-cs")), ("$c", cambioId), ("$var", varSal), ("$pre", precioSal), ("$cod", varSal));
                var cpId = Id("fx-cp");
                Ejecutar("INSERT INTO cambio_pago (id, cambio_id, direccion, medio, importe_minor, recibido_minor) VALUES ($id, $c, 'cobro', 'efectivo', $imp, $imp)",
                    ("$id", cpId), ("$c", cambioId), ("$imp", salidas - precioEnt));
            }

            nCambios++;
        }

        // Abonos: 50 (uno por cliente, con componente + aplicacion al cargo).
        for (var c = 0; c < 50; c++)
        {
            var cli = $"fx-c-{c:D3}";
            var abonoId = $"fx-ab-{c:D3}";
            var f = Fecha(30 + (c % 20), 11);
            Ejecutar("INSERT INTO cuenta_corriente_abono (id, numero, cliente_id, importe_minor, saldo_anterior_minor, saldo_posterior_minor, operacion_id, usuario_id, creado_en) VALUES ($id, $num, $cli, 5000, 100000, 95000, $op, 'fx-u1', $f)",
                ("$id", abonoId), ("$num", $"A-{(c + 1):D6}"), ("$cli", cli), ("$op", $"fx-opab-{c:D3}"), ("$f", f));
            Ejecutar("INSERT INTO cuenta_corriente_abono_componente (id, abono_id, medio, importe_minor, recibido_minor) VALUES ($id, $ab, 'efectivo', 5000, 5000)",
                ("$id", $"fx-abc-{c:D3}"), ("$ab", abonoId));
        }

        // Movimientos manuales de caja dispersos (~300, cada uno con su operación).
        // Todos en la sesión abierta: los triggers exigen sesión abierta.
        for (var m = 0; m < 300; m++)
        {
            var ses = sesiones[7];
            var f = Fecha((m * 3) % 61, 12, m % 60);
            var ingreso = m % 2 == 0;
            var mop = $"fx-mop-{m:D4}";
            Ejecutar("INSERT INTO caja_operacion (id, operacion_id, sesion_id, tipo, usuario_id, creado_en) VALUES ($id, $op, $s, 'movimiento_manual', 'fx-u1', $f)",
                ("$id", mop), ("$op", mop), ("$s", ses), ("$f", f));
            var categoria = ingreso ? "otro_ingreso" : (m % 10 == 9 ? "retiro_propietario" : "gasto");
            Ejecutar("INSERT INTO caja_movimiento (id, sesion_id, tipo, importe_minor, venta_id, motivo, creado_en, caja_operacion_id, pago_id, medio, categoria, usuario_id) VALUES ($id, $s, $tipo, $imp, NULL, 'Fx', $f, $mop, NULL, 'efectivo', $cat, 'fx-u1')",
                ("$id", Id("fx-cmm")), ("$s", ses), ("$tipo", ingreso ? "ingreso" : "egreso"),
                ("$imp", ingreso ? 2000 : -1000), ("$f", f), ("$mop", mop), ("$cat", categoria));
        }

        tx.Commit();
        return $"300 productos, 1200 variantes, {nVentas} ventas, {nLineas} líneas, {nCambios} cambios, 50 clientes/abonos";
    }
}
