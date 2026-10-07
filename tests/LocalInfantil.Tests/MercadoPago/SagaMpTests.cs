using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §50-§51: cobros, combinados, crash, cierre, anulación, F06, reportes.</summary>
public sealed class SagaMpTests
{
    private static async Task<(string V1, long Precio)> CatalogoAsync(EntornoMp e)
    {
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        return (v1, precio);
    }

    private static async Task<string> IniciarCobroAsync(EntornoMp e, string v1, long precio, long? importeMp = null)
    {
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        return await e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, importeMp ?? precio, null) },
            e.Sesion);
    }

    private static async Task<long> DisponibleAsync(EntornoMp e, string v1)
    {
        var codigo = (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, e.Sesion))!.CodigoInterno;
        return (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(codigo, e.Sesion))!.Disponible;
    }

    [Fact]
    public async Task Descubrir_pagina_y_no_depende_del_modelo()
    {
        using var e = await EntornoMp.CrearAsync();
        e.ResponderTerminales(total: 125);
        var terminales = await e.Descubrir.EjecutarAsync(e.Sesion);
        Assert.Equal(125, terminales.Count);
        Assert.Contains(terminales, t => t.Id == e.TerminalId);
        Assert.Equal(3, e.Fake.Requests.Count);
    }

    [Fact]
    public async Task Activar_pdv_exito_y_rechazo_con_mensaje_humano()
    {
        using var e = await EntornoMp.CrearAsync();
        e.Fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"TERM__SN1","pos_id":"P1","store_id":"S1","external_pos_id":null,"operating_mode":"STANDALONE"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        e.Fake.Responder("PATCH", "/terminals/v1/setup", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            """{"terminals":[{"id":"TERM__SN1","operating_mode":"PDV"}]}"""));
        var t = await e.Configurar.ActivarModoIntegradoAsync("TERM__SN1", e.Sesion);
        Assert.Equal("PDV", t.ModoOperativo);

        e.Fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"TERM__SN2","pos_id":"P1","store_id":"S1","external_pos_id":null,"operating_mode":"STANDALONE"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        e.Fake.Responder("PATCH", "/terminals/v1/setup", (_, _) => FakeMpHandler.Error(System.Net.HttpStatusCode.Forbidden, "terminal_not_allowed_action"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Configurar.ActivarModoIntegradoAsync("TERM__SN2", e.Sesion));
        Assert.Contains("Revisá la configuración del Point", ex.Message);
    }

    [Fact]
    public async Task Terminal_ya_pdv_no_llama_a_la_api()
    {
        using var e = await EntornoMp.CrearAsync();
        e.Fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"TERM__SN1","pos_id":"P1","store_id":"S1","external_pos_id":null,"operating_mode":"PDV"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        var t = await e.Configurar.ActivarModoIntegradoAsync("TERM__SN1", e.Sesion);
        Assert.Equal("PDV", t.ModoOperativo);
        Assert.DoesNotContain(e.Fake.Requests, r => r.Metodo == "PATCH");
    }

    [Fact]
    public async Task Cobro_100mp_confirma_venta_stock_caja_reportes()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        e.ResponderOrden("ORD000001", "processed");
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Confirmada, final.EstadoLocal);
        Assert.NotNull(final.VentaId);

        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(final.VentaId!, e.Sesion))!;
        Assert.Equal("confirmada", ficha.Venta.Estado);
        var pago = Assert.Single(ficha.Pagos);
        Assert.Equal(MediosPago.MercadoPago, pago.Medio);
        Assert.Equal(precio, pago.ImporteMinor);
        Assert.Equal(9, await DisponibleAsync(e, v1));

        var desde = DateTime.UtcNow.AddDays(-1).ToString("o");
        var hasta = DateTime.UtcNow.AddDays(1).ToString("o");
        var desglose = await e.Servicios.ConsultasReportes.DesgloseMediosAsync(desde, hasta, e.Sesion);
        Assert.Contains(desglose, d => d.Medio == MediosPago.MercadoPago && d.VentasMinor == precio);
    }

    [Fact]
    public async Task Cobro_combinado_mp_no_aumenta_efectivo()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var total = 2 * precio;
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        var op = await e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 2, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, 10000, 10000), new NuevoPago(MediosPago.MercadoPago, total - 10000, null) },
            e.Sesion);
        e.ResponderOrden("ORD000001", "processed");
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        Assert.True(final.EstadoLocal == EstadosLocalesMp.Confirmada, $"estado={final.EstadoLocal} mp={final.EstadoMp} error={final.UltimoError}");

        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(final.VentaId!, e.Sesion))!;
        Assert.Equal(2, ficha.Pagos.Count);
        Assert.Equal(total, ficha.Pagos.Sum(p => p.ImporteMinor));

        var esperado = await e.Servicios.CasoCerrarCaja.PrevisualizarAsync(10000, e.Sesion);
        Assert.Equal(10000, esperado.EfectivoEsperadoMinor);
    }

    [Fact]
    public async Task Cobro_mp_mas_transferencia_total_exacto_sin_duplicar_caja()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var total = 2 * precio;
        const long parteTransferencia = 10000;
        var parteMp = total - parteTransferencia;
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        var op = await e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 2, precio) },
            new[] { new NuevoPago(MediosPago.Transferencia, parteTransferencia, null), new NuevoPago(MediosPago.MercadoPago, parteMp, null) },
            e.Sesion);
        e.ResponderOrden("ORD000001", "processed");
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Confirmada, final.EstadoLocal);

        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Single(ventas);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(final.VentaId!, e.Sesion))!;
        Assert.Equal(2, ficha.Pagos.Count);
        Assert.Equal(total, ficha.Pagos.Sum(p => p.ImporteMinor));
        Assert.Equal(parteTransferencia, ficha.Pagos.Single(p => p.Medio == MediosPago.Transferencia).ImporteMinor);
        Assert.Equal(parteMp, ficha.Pagos.Single(p => p.Medio == MediosPago.MercadoPago).ImporteMinor);

        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(0, actual.EfectivoEsperadoMinor);
        Assert.Equal(parteMp, actual.ResumenMedios.Single(x => x.Medio == MediosPago.MercadoPago).ImporteMinor);
        Assert.Equal(parteTransferencia, actual.ResumenMedios.Single(x => x.Medio == MediosPago.Transferencia).ImporteMinor);
    }

    [Fact]
    public async Task Timeout_crear_no_duplica_y_reconcilia_con_misma_key()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        e.ResponderTerminales();
        e.Fake.Responder("POST", "/v1/orders", (_, _) => throw new TaskCanceledException("timeout simulado"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) },
            e.Sesion));
        Assert.Contains("Verificando el estado del cobro", ex.Message);

        e.ResponderCrearOrden("ORD000001", "processed");
        e.ResponderOrden("ORD000001", "processed");
        var resumen = await e.Reconciliar.EjecutarAsync(e.Sesion);
        Assert.Equal(0, resumen.PendientesDeVerificar);

        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Single(ventas);
        var keys = e.Fake.Requests.Where(r => r.Metodo == "POST").Select(r => r.IdempotencyKey).ToList();
        Assert.Single(keys.Distinct());
    }

    [Fact]
    public async Task Doble_consulta_concurrente_una_sola_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        e.ResponderOrden("ORD000001", "processed");
        var resultados = await Task.WhenAll(e.Consultar.EjecutarAsync(op, e.Sesion), e.Consultar.EjecutarAsync(op, e.Sesion));
        Assert.All(resultados, r => Assert.True(r.EstadoLocal is EstadosLocalesMp.Confirmada or EstadosLocalesMp.RequiereConciliacion));
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Confirmada, final.EstadoLocal);
        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Single(ventas);
    }

    [Fact]
    public async Task Crash_despues_processed_recupera_una_sola_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);

        await using (var conexion = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={e.RutaDb};Pooling=false"))
        {
            await conexion.OpenAsync();
            using var cmd = conexion.CreateCommand();
            cmd.CommandText = "UPDATE mercado_pago_operacion SET estado_local='procesada_remota', estado_mp='processed', order_id='ORD000001' WHERE operacion_id=$op;";
            cmd.Parameters.AddWithValue("$op", op);
            Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
        }

        e.ResponderOrden("ORD000001", "processed");
        var resumen = await e.Reconciliar.EjecutarAsync(e.Sesion);
        Assert.Equal(0, resumen.PendientesDeVerificar);

        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        var unica = Assert.Single(ventas);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(unica.Id, e.Sesion))!;
        Assert.Single(ficha.Pagos);
        Assert.Equal(9, await DisponibleAsync(e, v1));
    }

    [Fact]
    public async Task Cierre_bloqueado_con_pendiente_y_permitido_al_resolver()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        _ = await IniciarCobroAsync(e, v1, precio);
        var arqueo = await e.Servicios.CasoCerrarCaja.PrevisualizarAsync(0, e.Sesion);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId, 0, 0, null, $"op-cierre-mp-{Guid.NewGuid():N}", e.Sesion));
        Assert.Contains("pendiente de resolver", ex.Message);

        e.ResponderOrden("ORD000001", "failed");
        var resumen = await e.Reconciliar.EjecutarAsync(e.Sesion);
        Assert.Equal(0, resumen.PendientesDeVerificar);
        var cierre = await e.Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId, 0, 0, null, $"op-cierre-mp-{Guid.NewGuid():N}", e.Sesion);
        Assert.Equal(arqueo.SesionId, cierre.SesionId);
    }

    [Fact]
    public async Task Anulacion_bloqueada_con_pago_mp()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        e.ResponderOrden("ORD000001", "processed");
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoAnularVenta.EjecutarAsync(final.VentaId!, "test", $"op-anul-{Guid.NewGuid():N}", e.Sesion));
        Assert.Contains("devolución por Mercado Pago", ex.Message);
    }

    [Fact]
    public async Task Segundo_cobro_con_pendiente_se_rechaza()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        _ = await IniciarCobroAsync(e, v1, precio);
        e.ResponderCrearOrden("ORD000002");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) },
            e.Sesion));
        Assert.Contains("pendiente de resolver", ex.Message);
    }

    [Fact]
    public async Task Terminal_desaparecida_no_cobra()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        e.Fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"OTRA__SN9","pos_id":"P9","store_id":"S9","external_pos_id":null,"operating_mode":"PDV"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        e.ResponderCrearOrden();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) },
            e.Sesion));
        Assert.Contains("ya no aparece en Mercado Pago", ex.Message);
        Assert.DoesNotContain(e.Fake.Requests, r => r.Metodo == "POST");
    }

    [Fact]
    public async Task Sin_internet_efectivo_sigue_funcionando()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        e.Fake.Responder("GET", "/terminals/v1/list", (_, _) => throw new HttpRequestException("sin red"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) },
            e.Sesion));
        Assert.Contains("Revisá la conexión", ex.Message);

        var ok = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            $"op-ef-{Guid.NewGuid():N}", e.Sesion);
        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
    }

    [Fact]
    public async Task Cancelar_reintento_timeout_reusa_misma_key()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        var intentos = 0;
        e.Fake.Responder("POST", "/v1/orders/ORD000001/cancel", (_, _) =>
        {
            if (intentos++ == 0) throw new TaskCanceledException("timeout simulado");
            return FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
                """{"id":"ORD000001","status":"canceled","transactions":{"payments":[]}}""");
        });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Cancelar.EjecutarAsync(op, e.Sesion));
        Assert.Contains("Verificando", ex.Message);
        var final = await e.Cancelar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Cancelada, final.EstadoLocal);
        var cancels = e.Fake.Requests.Where(r => r.Metodo == "POST" && r.Url.EndsWith("/cancel")).ToList();
        Assert.Equal(2, cancels.Count);
        Assert.Single(cancels.Select(r => r.IdempotencyKey).Distinct());
    }

    [Fact]
    public async Task Cancelar_order_already_canceled_marca_cancelada()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        e.Fake.Responder("POST", "/v1/orders/ORD000001/cancel", (_, _) =>
            FakeMpHandler.Error(System.Net.HttpStatusCode.Conflict, "order_already_canceled"));
        var final = await e.Cancelar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.Cancelada, final.EstadoLocal);
        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Empty(ventas);
    }

    [Fact]
    public async Task Cancelar_cannot_cancel_reconcilia_sin_marcar()
    {
        using var e = await EntornoMp.CrearAsync();
        var (v1, precio) = await CatalogoAsync(e);
        var op = await IniciarCobroAsync(e, v1, precio);
        e.Fake.Responder("POST", "/v1/orders/ORD000001/cancel", (_, _) =>
            FakeMpHandler.Error(System.Net.HttpStatusCode.Conflict, "cannot_cancel_order"));
        e.ResponderOrden("ORD000001", "created");
        var final = await e.Cancelar.EjecutarAsync(op, e.Sesion);
        Assert.Equal(EstadosLocalesMp.RequiereConciliacion, final.EstadoLocal);
        var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Empty(ventas);
    }
}
