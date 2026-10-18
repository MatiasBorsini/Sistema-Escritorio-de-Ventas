using System.Globalization;
using System.Text;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Ventas;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// F12-C: importación del reporte de dinero liberado y conciliación.
/// Cubre el ejemplo obligatorio (venta 30.000 / bruto 30.000 / comisión 1.500
/// / neto 28.500) sin alterar venta, inventario ni caja, idempotencia,
/// reportes solapados, reportes parcialmente inválidos y conciliación manual
/// asistida.
/// </summary>
public sealed class ImportacionLiquidacionMpTests
{
    private const string Encabezado =
        "RECORD_TYPE,DESCRIPTION,DATE,SOURCE_ID,EXTERNAL_REFERENCE,ORDER_ID,GROSS_AMOUNT,NET_CREDIT_AMOUNT,NET_DEBIT_AMOUNT,MP_FEE_AMOUNT,TAXES_AMOUNT,CURRENCY";

    private static IReadOnlyList<NuevaLineaVenta> Lineas(string variante, long total, long unitario = 10000) => new[] { new NuevaLineaVenta(variante, total / unitario, unitario) };

    private static byte[] Bytes(string csv) => Encoding.UTF8.GetBytes(csv);

    private static string Monto(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FilaPago(string sourceId, string? externalRef, long grossMinor, long? netMinor = null, long? feeMinor = null, string? orderId = null, string? recordType = "release", string? descripcion = "payment")
    {
        string C(string? v) => v is null ? string.Empty : (v.Contains(',') ? $"\"{v}\"" : v);
        return string.Join(",",
            C(recordType), C(descripcion), "2026-01-05T10:00:00-03:00", C(sourceId), C(externalRef), C(orderId),
            Monto(grossMinor), netMinor is { } n ? Monto(n) : string.Empty, string.Empty,
            feeMinor is { } f ? Monto(f) : string.Empty, string.Empty, "ARS");
    }

    private static string Csv(params string[] filas) => Encabezado + "\n" + string.Join("\n", filas) + "\n";

    private static async Task<long> ContarAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<(string OperacionId, string VentaId, long Total)> VentaIntegradaAsync(EntornoMp e, string variante, long precio, string orderId = "ORD000001")
    {
        e.ResponderTerminales();
        e.ResponderCrearOrden(orderId, "processed");
        e.ResponderOrden(orderId, "processed");
        var operacionId = await e.Iniciar.EjecutarAsync(Lineas(variante, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, e.Sesion);
        var op = await e.Consultar.EjecutarAsync(operacionId, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Confirmada, op.EstadoLocal);
        Assert.False(string.IsNullOrEmpty(op.VentaId));
        return (operacionId, op.VentaId!, precio);
    }

    private static async Task<(string VentaId, long Total)> VentaManualAsync(EntornoMp e, string variante, long precio, string operacion, string? comprobante = null, bool confirmarDuplicado = false)
    {
        e.EstablecerModo(ModosPoint.Manual);
        var ok = await e.RegistrarManual.EjecutarAsync(Lineas(variante, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, operacion, comprobante, e.Sesion, confirmarDuplicado);
        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
        return (((ResultadoConfirmacionVenta.Confirmada)ok).VentaId, precio);
    }

    private static async Task<LiquidacionMpRegistrada> UnicaLiquidacionAsync(EntornoMp e)
    {
        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var liqs = await e.Servicios.RepoLiquidacionesMp.ListarLiquidacionesAsync(s, null, 10, 0);
        return Assert.Single(liqs);
    }

    [Fact]
    public async Task Concilia_venta_30000_bruto_30000_comision_1500_neto_28500_sin_alterar_venta_inventario_ni_caja()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, ventaId, _) = await VentaIntegradaAsync(e, v1, 30000);

        var stockAntes = await ContarAsync(e.RutaDb, $"SELECT COALESCE(SUM(cantidad),0) FROM movimiento_inventario WHERE variante_id='{v1}';");
        var cajaAntes = await ContarAsync(e.RutaDb, "SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta';");

        var csv = Csv(FilaPago("PAY-1", $"LI-{operacionId}", 30000, netMinor: 28500, feeMinor: 1500, orderId: "ORD000001"));

        var preview = await e.Previsualizar.EjecutarAsync(Bytes(csv), "reporte.csv", e.Sesion);
        Assert.False(preview.YaImportado);
        Assert.Equal(1, preview.FilasValidas);
        Assert.Equal(1, preview.FilasReconocidas);
        Assert.Equal(0, preview.FilasDuplicadas);

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "reporte.csv", "cuenta-123", e.Sesion);
        Assert.Equal(EstadosImportacionMp.Aplicado, resultado.Estado);
        Assert.Equal(1, resultado.FilasAplicadas);
        Assert.Equal(1, resultado.ConciliadasAuto);

        var liq = await UnicaLiquidacionAsync(e);
        Assert.Equal(EstadosConciliacionCobroManualMp.Conciliada, liq.EstadoConciliacion);
        Assert.Equal(ventaId, liq.VentaId);
        Assert.Equal(30000, liq.GrossMinor);
        Assert.Equal(1500, liq.MpFeeMinor);
        Assert.Equal(28500, liq.NetCreditoMinor);
        Assert.Equal(28500, liq.NetoMinor);
        Assert.Equal(TiposMatchConciliacionMp.ExactoExternalRef, (await ConciliacionRowAsync(e, liq.Id)).TipoMatch);

        // La importación NO altera venta, inventario ni caja.
        Assert.Equal(30000, await ContarAsync(e.RutaDb, $"SELECT total_minor FROM venta WHERE id='{ventaId}';"));
        Assert.Equal(stockAntes, await ContarAsync(e.RutaDb, $"SELECT COALESCE(SUM(cantidad),0) FROM movimiento_inventario WHERE variante_id='{v1}';"));
        Assert.Equal(cajaAntes, await ContarAsync(e.RutaDb, "SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta';"));
    }

    [Fact]
    public async Task Importar_dos_veces_el_mismo_archivo_no_duplica()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, _, _) = await VentaIntegradaAsync(e, v1, 30000);
        var csv = Csv(FilaPago("PAY-1", $"LI-{operacionId}", 30000, 28500, 1500));

        await e.Importar.EjecutarAsync(Bytes(csv), "reporte.csv", null, e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Importar.EjecutarAsync(Bytes(csv), "reporte.csv", null, e.Sesion));

        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_importacion;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
    }

    [Fact]
    public async Task Reportes_solapados_no_duplican_filas()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, _, _) = await VentaIntegradaAsync(e, v1, 30000);

        var filaCompartida = FilaPago("PAY-1", $"LI-{operacionId}", 30000, 28500, 1500);
        var archivoA = Csv(filaCompartida, FilaPago("PAY-2", null, 10000, 9500, 500));
        var archivoB = Csv(filaCompartida, FilaPago("PAY-3", null, 20000, 19000, 1000));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "enero.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "enero-bis.csv", null, e.Sesion);

        Assert.Equal(1, segundo.FilasDuplicadas);
        Assert.Equal(1, segundo.FilasAplicadas);
        Assert.Equal(3, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
    }

    [Fact]
    public async Task Reporte_parcialmente_invalido_se_registra_parcial()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var csv = Csv(
            FilaPago("PAY-1", null, 10000, 9500, 500),
            "release,payment,sin-fecha,P2,,,,300.00,285.00,,");

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "parcial.csv", null, e.Sesion);
        Assert.Equal(EstadosImportacionMp.Parcial, resultado.Estado);
        Assert.Equal(1, resultado.FilasAplicadas);
        Assert.Equal(1, resultado.FilasError);
    }

    [Fact]
    public async Task Cobro_manual_queda_pendiente_y_se_concilia_asistido()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        var (ventaId, _) = await VentaManualAsync(e, v1, 30000, "op-manual-conc", "TICKET-9");

        var csv = Csv(FilaPago("PAY-MANUAL", null, 30000, 28500, 1500));
        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "manual.csv", null, e.Sesion);
        Assert.Equal(0, resultado.ConciliadasAuto);

        var liq = await UnicaLiquidacionAsync(e);
        Assert.Equal(EstadosConciliacionCobroManualMp.Pendiente, liq.EstadoConciliacion);
        Assert.Null(liq.VentaId);

        var conciliado = await e.Conciliar.ConfirmarAsync(liq.Id, ventaId, e.Sesion);
        Assert.Equal(EstadosConciliacionCobroManualMp.Conciliada, conciliado.EstadoConciliacion);
        Assert.Equal(TiposMatchConciliacionMp.AsistidoHumano, conciliado.TipoMatch);

        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var cobro = await e.Servicios.RepoMercadoPago.ObtenerCobroManualPorVentaAsync(s, ventaId);
        Assert.Equal(EstadosConciliacionCobroManualMp.Conciliada, cobro!.EstadoConciliacion);
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
    }

    [Fact]
    public async Task Dos_ventas_mismo_importe_no_se_autovinculan()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VentaManualAsync(e, v1, 30000, "op-dos-1", "TK-A");
        await VentaManualAsync(e, v1, 30000, "op-dos-2", "TK-B", confirmarDuplicado: true);

        var csv = Csv(FilaPago("PAY-A", null, 30000, 28500, 1500), FilaPago("PAY-B", null, 30000, 28500, 1500));
        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "dos.csv", null, e.Sesion);

        Assert.Equal(0, resultado.ConciliadasAuto);
        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var liqs = await e.Servicios.RepoLiquidacionesMp.ListarLiquidacionesAsync(s, null, 10, 0);
        Assert.All(liqs, l => Assert.Equal(EstadosConciliacionCobroManualMp.Pendiente, l.EstadoConciliacion));
    }

    [Fact]
    public async Task Importe_bruto_distinto_marca_con_diferencia()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, ventaId, _) = await VentaIntegradaAsync(e, v1, 30000);

        var csv = Csv(FilaPago("PAY-1", $"LI-{operacionId}", 29000, 27500, 1500));
        await e.Importar.EjecutarAsync(Bytes(csv), "dif.csv", null, e.Sesion);

        var liq = await UnicaLiquidacionAsync(e);
        Assert.Equal(EstadosConciliacionCobroManualMp.ConDiferencia, liq.EstadoConciliacion);
        Assert.Equal(ventaId, liq.VentaId);
        Assert.Contains("no coincide", liq.Observacion ?? string.Empty);
    }

    [Fact]
    public async Task Impuestos_y_saldos_quedan_no_relacionados_y_desconocidos_requieren_revision()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var csv = Csv(
            "release,tax_payment,2026-01-05,TX1,,,,,5.00,,",
            "initial_available_balance,initial_available_balance,2026-01-01,,,,1000.00,,,,",
            "misterio,cosa_rara,2026-01-02,XX,,,,,,");

        await e.Importar.EjecutarAsync(Bytes(csv), "varios.csv", null, e.Sesion);

        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var liqs = await e.Servicios.RepoLiquidacionesMp.ListarLiquidacionesAsync(s, null, 10, 0);
        Assert.Equal(3, liqs.Count);
        Assert.Contains(liqs, l => l.Clasificacion == ClasificacionMovimientoMp.Impuesto && l.EstadoConciliacion == EstadosConciliacionCobroManualMp.NoRelacionada);
        Assert.Contains(liqs, l => l.Clasificacion == ClasificacionMovimientoMp.Saldo && l.EstadoConciliacion == EstadosConciliacionCobroManualMp.NoRelacionada);
        Assert.Contains(liqs, l => l.Clasificacion == ClasificacionMovimientoMp.Desconocido && l.EstadoConciliacion == EstadosConciliacionCobroManualMp.RequiereIntervencion);
    }

    [Fact]
    public async Task Devolucion_sin_reembolso_local_requiere_revision()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var csv = Csv("release,refund,2026-01-05,RF-1,,,,,300.00,,");

        await e.Importar.EjecutarAsync(Bytes(csv), "refund.csv", null, e.Sesion);

        var liq = await UnicaLiquidacionAsync(e);
        Assert.Equal(ClasificacionMovimientoMp.Devolucion, liq.Clasificacion);
        Assert.Equal(EstadosConciliacionCobroManualMp.RequiereIntervencion, liq.EstadoConciliacion);
    }

    [Fact]
    public async Task Sin_permiso_de_conciliar_no_se_puede_previsualizar_ni_importar()
    {
        using var e = await EntornoMp.CrearAsync();
        await e.Servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("emple", RolesLocal.Empleada), "clave-emple-000000", e.Sesion);
        var sesionEmpleada = await e.Servicios.CasoIniciarSesion.EjecutarAsync("emple", "clave-emple-000000");

        var csv = Csv(FilaPago("PAY-1", null, 10000, 9500, 500));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Previsualizar.EjecutarAsync(Bytes(csv), "r.csv", sesionEmpleada.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Importar.EjecutarAsync(Bytes(csv), "r.csv", null, sesionEmpleada.IdSesion));
        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_importacion;"));
    }

    private sealed record ConciliacionRow(string TipoMatch);

    private static async Task<ConciliacionRow> ConciliacionRowAsync(EntornoMp e, string liquidacionId)
    {
        await using var conexion = new SqliteConnection($"Data Source={e.RutaDb};Pooling=false");
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT tipo_match FROM mercado_pago_conciliacion WHERE liquidacion_id = $liq;";
        cmd.Parameters.AddWithValue("$liq", liquidacionId);
        var tipo = (string)(await cmd.ExecuteScalarAsync())!;
        return new ConciliacionRow(tipo);
    }
}
