using System.Net;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.MercadoPago;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §49: terminales, paginación, setup, orders, errores, timeout. Sin Internet.</summary>
public sealed class ClienteMpTests
{
    private static (FakeMpHandler Fake, MercadoPagoPointClient Cliente) Crear()
    {
        var fake = new FakeMpHandler();
        return (fake, new MercadoPagoPointClient(new HttpClient(fake), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Listar_pagina_terminales_mapea_modos_sin_depender_del_modelo()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"NEWLAND_N950__N950NCB801293324","pos_id":"23545678","store_id":"12354567","external_pos_id":"SUC0101POS","operating_mode":"PDV"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        var pagina = await cliente.ListarTerminalesAsync("T", 50, 0);
        var t = Assert.Single(pagina.Terminales);
        Assert.Equal("NEWLAND_N950__N950NCB801293324", t.Id);
        Assert.Equal("23545678", t.PosId);
        Assert.Equal("12354567", t.SucursalId);
        Assert.Equal("SUC0101POS", t.PosExternoId);
        Assert.Equal("PDV", t.ModoOperativo);
    }

    [Fact]
    public async Task Listar_con_store_id_string_y_modo_desconocido()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"data":{"terminals":[{"id":"FUTURO_X1__SN1","pos_id":null,"store_id":99,"external_pos_id":null,"operating_mode":"UNDEFINED"}]},"paging":{"total":1,"offset":0,"limit":50}}"""));
        var pagina = await cliente.ListarTerminalesAsync("T", 50, 0);
        var t = Assert.Single(pagina.Terminales);
        Assert.Equal("UNDEFINED", t.ModoOperativo);
        Assert.Equal("99", t.SucursalId);
    }

    [Fact]
    public async Task Listar_sin_terminales_devuelve_vacio()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"data":{"terminals":[]},"paging":{"total":0,"offset":0,"limit":50}}"""));
        var pagina = await cliente.ListarTerminalesAsync("T", 50, 0);
        Assert.Empty(pagina.Terminales);
        Assert.Equal(0, pagina.Total);
    }

    [Fact]
    public async Task Crear_orden_envia_payload_oficial_e_idempotencia()
    {
        var (fake, cliente) = Crear();
        fake.Responder("POST", "/v1/orders", (_, _) => FakeMpHandler.Json(HttpStatusCode.Created,
            """{"id":"ORD1","type":"point","status":"created","external_reference":"LI-x","transactions":{"payments":[{"id":"PAY1","amount":"100.00","status":"created"}]}}"""));
        var orden = await cliente.CrearOrdenAsync("T", "KEY-1", "LI-x", 10000, "TERM__SN1", "Venta local");
        Assert.Equal("ORD1", orden.Id);
        Assert.Equal("created", orden.Estado);
        var req = Assert.Single(fake.Requests);
        Assert.Equal("KEY-1", req.IdempotencyKey);
        Assert.Contains("\"type\":\"point\"", req.Cuerpo);
        Assert.Contains("\"external_reference\":\"LI-x\"", req.Cuerpo);
        Assert.Contains("\"amount\":\"100.00\"", req.Cuerpo);
        Assert.Contains("\"terminal_id\":\"TERM__SN1\"", req.Cuerpo);
        Assert.DoesNotContain("payment_method", req.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("default_type", req.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installments", req.Cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("installments_cost", req.Cuerpo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Obtener_orden_descarta_metadatos_de_plan_de_pago_no_persistidos()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/v1/orders/ORD1", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"id":"ORD1","status":"processed","status_detail":"accredited","external_reference":"LI-x","transactions":{"payments":[{"id":"PAY1","amount":"100.00","status":"approved","payment_method":{"id":"visa","type":"credit_card","installments":6,"card":{"last_digits":"1234"}}}]}}"""));

        var orden = await cliente.ObtenerOrdenAsync("TOKEN", "ORD1");

        Assert.Equal("ORD1", orden.Id);
        Assert.Equal("processed", orden.Estado);
        Assert.Equal("accredited", orden.DetalleEstado);
        Assert.Equal("LI-x", orden.ReferenciaExterna);
        Assert.Equal(new PagoOrdenMp("PAY1", "approved", "100.00"), Assert.Single(orden.Pagos));
    }

    [Fact]
    public async Task Cancelar_en_terminal_envia_header_condicional()
    {
        var (fake, cliente) = Crear();
        fake.Responder("POST", "/v1/orders/ORD1/cancel", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"id":"ORD1","status":"canceled","transactions":{"payments":[]}}"""));
        var orden = await cliente.CancelarOrdenAsync("T", "K", "ORD1", enTerminal: true);
        Assert.Equal("canceled", orden.Estado);
    }

    [Fact]
    public async Task Reembolsar_total_y_parcial()
    {
        var (fake, cliente) = Crear();
        fake.Responder("POST", "/v1/orders/ORD1/refund", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"id":"ORD1","status":"refunded","transactions":{"payments":[]}}"""));
        var total = await cliente.ReembolsarOrdenAsync("T", "K1", "ORD1", null);
        Assert.Equal("refunded", total.Estado);
        var parcial = await cliente.ReembolsarOrdenAsync("T", "K2", "ORD1", 2500);
        Assert.Equal("refunded", parcial.Estado);
        Assert.Contains(fake.Requests, r => r.Cuerpo != null && r.Cuerpo.Contains("\"amount\":\"25.00\""));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, "Credencial de Mercado Pago inválida o vencida.")]
    [InlineData(HttpStatusCode.Forbidden, "terminal_not_allowed_action", "Mercado Pago no permite cambiar automáticamente el modo de esta terminal. Revisá la configuración del Point.")]
    [InlineData(HttpStatusCode.Forbidden, null, "Sin permiso en Mercado Pago")]
    [InlineData(HttpStatusCode.NotFound, null, "no se encontró en Mercado Pago")]
    [InlineData(HttpStatusCode.Conflict, "already_queued_order_for_terminal", "ya tiene un cobro pendiente")]
    [InlineData(HttpStatusCode.InternalServerError, null, "Verificá el estado antes de reintentar")]
    [InlineData(HttpStatusCode.BadRequest, null, "No se gener")]
    [InlineData(HttpStatusCode.PreconditionFailed, null, "sin asumir resultado")]
    public async Task Errores_http_con_mensaje_humano(HttpStatusCode status, string? codigo, string fragmento)
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Error(status, codigo ?? "x"));
        var ex = await Assert.ThrowsAsync<ExcepcionMercadoPago>(() => cliente.ListarTerminalesAsync("T", 1, 0));
        Assert.Contains(fragmento, ex.Message);
        Assert.DoesNotContain("T", ex.Message);
    }

    [Fact]
    public async Task Respuesta_invalida_con_mensaje_humano()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK, "no-json"));
        var ex = await Assert.ThrowsAsync<ExcepcionMercadoPago>(() => cliente.ListarTerminalesAsync("T", 1, 0));
        Assert.Equal("respuesta_invalida", ex.Codigo);
    }

    [Fact]
    public async Task Offline_propaga_http_sin_inventar_estado()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/v1/orders/ORD1", (_, _) => throw new HttpRequestException("sin red"));
        await Assert.ThrowsAsync<HttpRequestException>(() => cliente.ObtenerOrdenAsync("T", "ORD1"));
    }

    [Fact]
    public async Task Nunca_envia_authorization_en_cuerpo_ni_lo_expone()
    {
        var (fake, cliente) = Crear();
        fake.Responder("GET", "/terminals/v1/list", (_, _) => FakeMpHandler.Json(HttpStatusCode.OK,
            """{"data":{"terminals":[]},"paging":{"total":0,"offset":0,"limit":50}}"""));
        await cliente.ListarTerminalesAsync("SECRETO-ABC", 1, 0);
        var req = Assert.Single(fake.Requests);
        Assert.DoesNotContain("SECRETO-ABC", req.Cuerpo ?? string.Empty);
        Assert.DoesNotContain("SECRETO-ABC", req.Url);
    }
}
