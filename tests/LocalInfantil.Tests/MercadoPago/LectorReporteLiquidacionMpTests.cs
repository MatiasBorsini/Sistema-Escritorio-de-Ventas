using System.Text;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.MercadoPago;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// F12-B/C: lector puro del reporte de dinero liberado. No toca la base.
/// Se prueban validación de encabezados, separadores, decimales, fechas,
/// clasificación de movimientos, errores por fila y hash de deduplicación.
/// </summary>
public sealed class LectorReporteLiquidacionMpTests
{
    private const string Encabezado =
        "RECORD_TYPE,DESCRIPTION,DATE,SOURCE_ID,EXTERNAL_REFERENCE,ORDER_ID,GROSS_AMOUNT,NET_CREDIT_AMOUNT,NET_DEBIT_AMOUNT,MP_FEE_AMOUNT,TAXES_AMOUNT,CURRENCY";

    [Fact]
    public void Falta_columna_obligatoria_es_un_formato_invalido()
    {
        var csv = "DESCRIPTION,DATE\npayment,2026-01-05\n";
        Assert.Throws<FormatoReporteInvalidoMpException>(() => LectorReporteLiquidacionMp.Parsear(csv, "r.csv"));
    }

    [Fact]
    public void Pago_se_parsea_a_centavos_y_clasifica_como_cobro()
    {
        var csv = Encabezado + "\n" +
                  "release,payment,2026-01-05T10:00:00-03:00,PAY123,LI-op-1,ORD1,300.00,285.00,,15.00,,ARS\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        var fila = Assert.Single(parseo.Filas);
        Assert.Equal(ClasificacionMovimientoMp.Cobro, fila.Clasificacion);
        Assert.Equal(30000, fila.GrossMinor);
        Assert.Equal(28500, fila.NetCreditoMinor);
        Assert.Equal(1500, fila.MpFeeMinor);
        Assert.Equal("LI-op-1", fila.ExternalReference);
        Assert.Equal("PAY123", fila.SourceId);
    }

    [Fact]
    public void Devolucion_contracargo_e_impuestos_se_clasifican_aparte()
    {
        var csv = Encabezado + "\n" +
                  "release,refund,2026-01-05,RF1,,,,,300.00,,\n" +
                  "chargeback,chargeback,2026-01-06,CB1,,,300.00,,,\n" +
                  "release,tax_payment,2026-01-07,TX1,,,,,5.00,,\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        Assert.Equal(3, parseo.Filas.Count);
        Assert.Equal(ClasificacionMovimientoMp.Devolucion, parseo.Filas[0].Clasificacion);
        Assert.Equal(ClasificacionMovimientoMp.Contracargo, parseo.Filas[1].Clasificacion);
        Assert.Equal(ClasificacionMovimientoMp.Impuesto, parseo.Filas[2].Clasificacion);
    }

    [Fact]
    public void Saldos_y_transferencias_no_son_ventas()
    {
        var csv = Encabezado + "\n" +
                  "initial_available_balance,initial_available_balance,2026-01-01,,,,1000.00,,,,\n" +
                  "payout,payout,2026-01-02,,,,2500.00,,2500.00,,\n" +
                  "unknown_thing,misterio,2026-01-03,,,,,,\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        Assert.Equal(ClasificacionMovimientoMp.Saldo, parseo.Filas[0].Clasificacion);
        Assert.Equal(ClasificacionMovimientoMp.Transferencia, parseo.Filas[1].Clasificacion);
        Assert.Equal(ClasificacionMovimientoMp.Desconocido, parseo.Filas[2].Clasificacion);
        Assert.Equal(1, parseo.FilasDesconocidas);
    }

    [Fact]
    public void Monto_con_mas_de_dos_decimales_es_error_de_fila_sin_abortar_el_archivo()
    {
        var csv = Encabezado + "\n" +
                  "release,payment,2026-01-05,P1,,,,300.123,300.12,,\n" +
                  "release,payment,2026-01-05,P2,,,,300.00,285.00,,\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        var error = Assert.Single(parseo.Errores);
        Assert.Equal(2, error.NumeroFila);
        Assert.Single(parseo.Filas);
        Assert.Equal("P2", parseo.Filas[0].SourceId);
    }

    [Fact]
    public void Fecha_invalida_es_error_de_fila()
    {
        var csv = Encabezado + "\n" +
                  "release,payment,not-a-date,P1,,,,300.00,285.00,,\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        Assert.Empty(parseo.Filas);
        Assert.Single(parseo.Errores);
    }

    [Fact]
    public void Delimitador_punto_y_coma_se_detecta()
    {
        const string csv = "RECORD_TYPE;DESCRIPTION;DATE;GROSS_AMOUNT\nrelease;payment;2026-01-05;300.00\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        var fila = Assert.Single(parseo.Filas);
        Assert.Equal(30000, fila.GrossMinor);
        Assert.Contains(";", parseo.FormatoReporte);
    }

    [Fact]
    public void Campos_entrecomillados_con_comas_y_saltos_se_respetan()
    {
        var csv = Encabezado + "\n" +
                  "release,\"payment, con nota\",2026-01-05T10:00:00-03:00,P1,,,300.00,285.00,,,,ARS\n";
        var parseo = LectorReporteLiquidacionMp.Parsear(csv, "r.csv");

        var fila = Assert.Single(parseo.Filas);
        Assert.Equal("payment, con nota", fila.Descripcion);
        Assert.Equal(30000, fila.GrossMinor);
    }

    [Fact]
    public void Hash_de_fila_es_estable_para_la_misma_fila()
    {
        var linea = "release,payment,2026-01-05,P1,,,,300.00,285.00,,\n";
        var a = LectorReporteLiquidacionMp.Parsear(Encabezado + "\n" + linea, "r1.csv").Filas[0];
        var b = LectorReporteLiquidacionMp.Parsear(Encabezado + "\n" + linea, "r2.csv").Filas[0];
        Assert.Equal(a.FilaHash, b.FilaHash);
    }

    [Fact]
    public void Contenido_con_BOM_UTF8_se_decodifica()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Encabezado + "\nrelease,payment,2026-01-05,P1,,,,300.00,285.00,,\n")).ToArray();
        var texto = LectorReporteLiquidacionMp.Decodificar(bytes);
        var parseo = LectorReporteLiquidacionMp.Parsear(texto, "r.csv");
        Assert.Single(parseo.Filas);
    }

    [Fact]
    public void Hash_de_archivo_es_determinista()
    {
        var bytes = Encoding.UTF8.GetBytes(Encabezado);
        Assert.Equal(LectorReporteLiquidacionMp.HashArchivo(bytes), LectorReporteLiquidacionMp.HashArchivo(bytes));
        Assert.Equal(64, LectorReporteLiquidacionMp.HashArchivo(bytes).Length);
    }
}
