using System.Globalization;
using System.Text;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Ventas;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// F12-B (D-50): deduplicación correcta del reporte de liquidación.
/// - Identidad de fila = (importación, ordinal): dos filas idénticas del mismo
///   archivo se conservan; nunca se descarta una fila en silencio.
/// - Dedup entre reportes solapados SOLO por identidad remota confiable.
/// - Filas ambiguas (sin identidad) con contenido repetido quedan en revisión.
/// - No se duplica el vínculo con una venta ni el reconocimiento de un importe.
/// </summary>
public sealed class DeduplicacionFilasLiquidacionMpTests
{
    private const string Encabezado =
        "RECORD_TYPE,DESCRIPTION,DATE,SOURCE_ID,EXTERNAL_REFERENCE,ORDER_ID,GROSS_AMOUNT,NET_CREDIT_AMOUNT,NET_DEBIT_AMOUNT,MP_FEE_AMOUNT,TAXES_AMOUNT,CURRENCY";

    private static byte[] Bytes(string csv) => Encoding.UTF8.GetBytes(csv);

    private static string Monto(long minor) => (minor / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string FilaPago(string? sourceId, string? externalRef, long grossMinor, long? netMinor = null, long? feeMinor = null, string? orderId = null, string? recordType = "release", string? descripcion = "payment")
    {
        string C(string? v) => v is null ? string.Empty : (v.Contains(',') ? $"\"{v}\"" : v);
        return string.Join(",",
            C(recordType), C(descripcion), "2026-01-05T10:00:00-03:00", C(sourceId), C(externalRef), C(orderId),
            Monto(grossMinor), netMinor is { } n ? Monto(n) : string.Empty, string.Empty,
            feeMinor is { } f ? Monto(f) : string.Empty, string.Empty, "ARS");
    }

    private static string FilaReverso(string sourceId, string? externalRef, long montoMinor, string? orderId = null)
    {
        string C(string? v) => v is null ? string.Empty : (v.Contains(',') ? $"\"{v}\"" : v);
        return string.Join(",",
            "refund", "refund", "2026-01-06T10:00:00-03:00", C(sourceId), C(externalRef), C(orderId),
            string.Empty, string.Empty, Monto(montoMinor), string.Empty, string.Empty, "ARS");
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

    private static Task<long> ContarAsync(EntornoMp e, string sql) => ContarAsync(e.RutaDb, sql);

    private static async Task<(string OperacionId, string VentaId)> VentaIntegradaAsync(EntornoMp e, string variante, long precio, string orderId = "ORD000001")
    {
        e.ResponderTerminales();
        e.ResponderCrearOrden(orderId, "processed");
        e.ResponderOrden(orderId, "processed");
        var operacionId = await e.Iniciar.EjecutarAsync(new[] { new NuevaLineaVenta(variante, precio / 10000, 10000) }, new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, e.Sesion);
        var op = await e.Consultar.EjecutarAsync(operacionId, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Confirmada, op.EstadoLocal);
        return (operacionId, op.VentaId!);
    }

    // A. Dos filas idénticas en un mismo CSV: ambas se conservan.
    [Fact]
    public async Task A_Dos_filas_identicas_en_un_mismo_csv_se_conservan()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();

        var filaIgual = FilaPago("PAY-1", null, 30000, 28500, 1500);
        var csv = Csv(filaIgual, filaIgual);

        var preview = await e.Previsualizar.EjecutarAsync(Bytes(csv), "dup.csv", e.Sesion);
        Assert.Equal(2, preview.FilasValidas);

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "dup.csv", null, e.Sesion);
        Assert.Equal(2, resultado.FilasAplicadas);
        Assert.Equal(0, resultado.FilasDuplicadas);
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        // Ambas filas conservan su ordinal y una huella de contenido idéntica.
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(DISTINCT fila_ordinal) FROM mercado_pago_liquidacion;"));
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(DISTINCT fila_hash) FROM mercado_pago_liquidacion;"));
    }

    // B. Reimportar exactamente el mismo archivo no duplica.
    [Fact]
    public async Task B_Reimportar_el_mismo_archivo_no_duplica()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var csv = Csv(FilaPago("PAY-1", null, 30000, 28500, 1500));

        await e.Importar.EjecutarAsync(Bytes(csv), "r.csv", null, e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Importar.EjecutarAsync(Bytes(csv), "r.csv", null, e.Sesion));

        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_importacion;"));
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
    }

    // C. Dos archivos superpuestos con identidad remota: no duplican movimientos.
    [Fact]
    public async Task C_Reportes_superpuestos_con_identidad_remota_no_duplican()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, _) = await VentaIntegradaAsync(e, v1, 30000);

        var compartida = FilaPago("PAY-1", $"LI-{operacionId}", 30000, 28500, 1500);
        var archivoA = Csv(compartida, FilaPago("PAY-2", null, 10000, 9500, 500));
        var archivoB = Csv(compartida, FilaPago("PAY-3", null, 20000, 19000, 1000));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "enero.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "enero-bis.csv", null, e.Sesion);

        Assert.Equal(1, segundo.FilasDuplicadas);
        Assert.Equal(1, segundo.FilasAplicadas);
        Assert.Equal(3, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        // La identidad remota se basa en el id de movimiento (SOURCE_ID), no en
        // la referencia de la venta; la fila compartida se guarda una sola vez.
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE identidad_remota='C:PAY-1';"));
    }

    // C2. Dos movimientos distintos con la MISMA EXTERNAL_REFERENCE de la venta:
    // la referencia comercial no es la identidad del movimiento; no se eliminan.
    [Fact]
    public async Task C2_Cobros_distintos_con_misma_external_reference_no_se_eliminan()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();

        var archivoA = Csv(FilaPago("PAY-1", "LI-OP-COMPARTIDA", 30000, 28500, 1500));
        var archivoB = Csv(FilaPago("PAY-2", "LI-OP-COMPARTIDA", 20000, 19000, 1000));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "venta-a.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "venta-b.csv", null, e.Sesion);

        Assert.Equal(0, segundo.FilasDuplicadas);
        Assert.Equal(1, segundo.FilasAplicadas);
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
    }

    // C3. Dos movimientos distintos de la MISMA orden (sin SOURCE_ID): la orden
    // comercial no es la identidad del movimiento; no se colapsan.
    [Fact]
    public async Task C3_Movimientos_distintos_de_la_misma_orden_no_se_colapsan()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();

        var archivoA = Csv(FilaPago(null, null, 30000, 28500, 1500, orderId: "ORD-9"));
        var archivoB = Csv(FilaPago(null, null, 20000, 19000, 1000, orderId: "ORD-9"));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "orden-a.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "orden-b.csv", null, e.Sesion);

        Assert.Equal(0, segundo.FilasDuplicadas);
        Assert.Equal(1, segundo.FilasAplicadas);
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE identidad_remota IS NULL;"));
    }

    // C4. Dos devoluciones parciales distintas de la misma venta se conservan.
    [Fact]
    public async Task C4_Dos_devoluciones_parciales_distintas_se_conservan()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();

        var archivoA = Csv(FilaReverso("REF-1", "LI-OP-COMPARTIDA", 5000));
        var archivoB = Csv(FilaReverso("REF-2", "LI-OP-COMPARTIDA", 3000));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "ref-a.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "ref-b.csv", null, e.Sesion);

        Assert.Equal(0, segundo.FilasDuplicadas);
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE identidad_remota='R:REF-1';"));
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE identidad_remota='R:REF-2';"));
    }

    // D. Dos archivos con filas ambiguas (sin identidad): requieren revisión.
    [Fact]
    public async Task D_Filas_ambiguas_entre_reportes_quedan_en_revision()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();

        var ambigua = FilaPago(null, null, 30000, 28500, 1500); // sin identidad remota
        var archivoA = Csv(ambigua);
        var archivoB = Csv(ambigua, FilaPago("PAY-9", null, 20000, 19000, 1000));

        await e.Importar.EjecutarAsync(Bytes(archivoA), "a.csv", null, e.Sesion);
        var segundo = await e.Importar.EjecutarAsync(Bytes(archivoB), "b.csv", null, e.Sesion);

        // La fila ambigua no se descarta: se conserva y queda en revisión.
        Assert.Equal(1, segundo.FilasEnRevision);
        Assert.Equal(3, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        Assert.Equal(1, await ContarAsync(e, $"SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE estado_conciliacion='{EstadosConciliacionCobroManualMp.RequiereIntervencion}' AND identidad_remota IS NULL;"));
        // La fila distinta del segundo archivo sí queda pendiente normal.
        Assert.Equal(0, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
    }

    // E. Dos cobros legítimos con igual importe: no se eliminan.
    [Fact]
    public async Task E_Dos_cobros_legitimos_con_igual_importe_no_se_eliminan()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var csv = Csv(
            FilaPago("PAY-A", null, 30000, 28500, 1500),
            FilaPago("PAY-B", null, 30000, 28500, 1500));

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "iguales.csv", null, e.Sesion);

        Assert.Equal(2, resultado.FilasAplicadas);
        Assert.Equal(0, resultado.FilasDuplicadas);
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        Assert.Equal(2, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE gross_minor=30000;"));
    }

    // §6: dos filas a la MISMA venta no duplican el vínculo ni el reconocimiento.
    [Fact]
    public async Task Dos_filas_a_la_misma_venta_no_duplican_vinculo()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, _) = await VentaIntegradaAsync(e, v1, 30000);

        var fila = FilaPago("PAY-1", $"LI-{operacionId}", 30000, 28500, 1500);
        var csv = Csv(fila, fila);

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "misma.csv", null, e.Sesion);

        Assert.Equal(1, resultado.ConciliadasAuto);
        Assert.Equal(1, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
        Assert.Equal(1, await ContarAsync(e, $"SELECT COUNT(*) FROM mercado_pago_liquidacion WHERE estado_conciliacion='{EstadosConciliacionCobroManualMp.RequiereIntervencion}';"));
    }

    // G. Importación rechazada (sin filas válidas) no deja datos parciales.
    [Fact]
    public async Task G_Importacion_rechazada_no_deja_datos_parciales()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        // Fila sin fecha: no se reconocen movimientos válidos.
        var csv = Encabezado + "\nrelease,payment,no-es-fecha,P1,,,,300.00,285.00,,\n";

        await Assert.ThrowsAsync<FormatoReporteInvalidoMpException>(
            () => e.Importar.EjecutarAsync(Bytes(csv), "malo.csv", null, e.Sesion));

        Assert.Equal(0, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_importacion;"));
        Assert.Equal(0, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_liquidacion;"));
        Assert.Equal(0, await ContarAsync(e, "SELECT COUNT(*) FROM mercado_pago_conciliacion;"));
    }

    // H. Ejemplo obligatorio: venta 30.000 / bruto 30.000 / comisión 1.500 / neto 28.500.
    [Fact]
    public async Task H_Venta_30000_comision_1500_neto_28500_sin_alterar_venta_ni_caja()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var (operacionId, ventaId) = await VentaIntegradaAsync(e, v1, 30000);

        var cajaAntes = await ContarAsync(e, "SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta';");
        var csv = Csv(FilaPago("PAY-1", $"LI-{operacionId}", 30000, 28500, 1500));

        var resultado = await e.Importar.EjecutarAsync(Bytes(csv), "ejemplo.csv", null, e.Sesion);
        Assert.Equal(1, resultado.ConciliadasAuto);

        Assert.Equal(1, await ContarAsync(e, "SELECT fila_ordinal FROM mercado_pago_liquidacion;"));
        Assert.Equal(30000, await ContarAsync(e, "SELECT gross_minor FROM mercado_pago_liquidacion;"));
        Assert.Equal(1500, await ContarAsync(e, "SELECT mp_fee_minor FROM mercado_pago_liquidacion;"));
        Assert.Equal(28500, await ContarAsync(e, "SELECT COALESCE(net_credito_minor,0) - COALESCE(net_debito_minor,0) FROM mercado_pago_liquidacion;"));
        Assert.Equal(30000, await ContarAsync(e, $"SELECT total_minor FROM venta WHERE id='{ventaId}';"));
        Assert.Equal(cajaAntes, await ContarAsync(e, "SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta';"));
    }
}
