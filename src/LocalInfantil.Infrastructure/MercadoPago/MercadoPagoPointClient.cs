using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.MercadoPago;

namespace LocalInfantil.Infrastructure.MercadoPago;

/// <summary>
/// Cliente HTTP directo contra Orders API / Terminals API (sin SDK).
/// Nunca loguea Authorization ni guarda el token. Los errores se traducen
/// a mensajes humanos sin secretos ni cuerpos crudos.
/// </summary>
public sealed class MercadoPagoPointClient : IMercadoPagoPointClient
{
    public const string BaseUrl = "https://api.mercadopago.com";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public MercadoPagoPointClient(HttpClient http, TimeSpan? timeout = null)
    {
        _http = http;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(BaseUrl);
        if (timeout.HasValue)
            _http.Timeout = timeout.Value;
    }

    public async Task<PaginaTerminales> ListarTerminalesAsync(string accessToken, int limite, int desplazamiento, CancellationToken cancellationToken = default)
    {
        var url = $"/terminals/v1/list?limit={Math.Clamp(limite, 1, 100)}&offset={Math.Max(0, desplazamiento)}";
        using var respuesta = await EnviarAsync(HttpMethod.Get, url, accessToken, null, null, cancellationToken);
        var doc = await LeerJsonAsync(respuesta, cancellationToken);
        var root = doc.RootElement;
        var terminales = new List<TerminalPoint>();
        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("terminals", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                terminales.Add(new TerminalPoint(
                    t.GetProperty("id").GetString() ?? string.Empty,
                    t.TryGetProperty("pos_id", out var p) ? p.GetString() : null,
                    t.TryGetProperty("store_id", out var s) ? s.ValueKind == JsonValueKind.String ? s.GetString() : s.GetRawText() : null,
                    t.TryGetProperty("external_pos_id", out var e) ? e.GetString() : null,
                    t.TryGetProperty("operating_mode", out var m) ? m.GetString() ?? "UNDEFINED" : "UNDEFINED"));
            }
        }

        long total = terminales.Count;
        int lim = limite, desp = desplazamiento;
        if (root.TryGetProperty("paging", out var paging))
        {
            total = paging.TryGetProperty("total", out var t) && t.TryGetInt64(out var v) ? v : total;
            lim = paging.TryGetProperty("limit", out var l) && l.TryGetInt32(out var lv) ? lv : lim;
            desp = paging.TryGetProperty("offset", out var o) && o.TryGetInt32(out var ov) ? ov : desp;
        }

        return new PaginaTerminales(terminales, total, lim, desp);
    }

    public async Task<TerminalPoint> ConfigurarModoAsync(string accessToken, string terminalId, string modo, CancellationToken cancellationToken = default)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            terminals = new[] { new { id = terminalId, operating_mode = modo } },
        });
        using var respuesta = await EnviarAsync(new HttpMethod("PATCH"), "/terminals/v1/setup", accessToken, cuerpo, null, cancellationToken);
        var doc = await LeerJsonAsync(respuesta, cancellationToken);
        var root = doc.RootElement;
        if (root.TryGetProperty("terminals", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in arr.EnumerateArray())
            {
                if (t.TryGetProperty("id", out var id) && id.GetString() == terminalId)
                {
                    var modoFinal = t.TryGetProperty("operating_mode", out var m) ? m.GetString() ?? modo : modo;
                    return new TerminalPoint(terminalId, null, null, null, modoFinal);
                }
            }
        }

        return new TerminalPoint(terminalId, null, null, null, modo);
    }

    public async Task<OrdenMp> CrearOrdenAsync(string accessToken, string idempotencyKey, string externalReference, long importeMinor, string terminalId, string descripcion, CancellationToken cancellationToken = default)
    {
        var cuerpo = JsonSerializer.Serialize(new
        {
            type = "point",
            external_reference = externalReference,
            expiration_time = "PT15M",
            transactions = new
            {
                payments = new[] { new { amount = ConvertidorMontosMp.ATexto(importeMinor) } },
            },
            config = new
            {
                point = new { terminal_id = terminalId, print_on_terminal = "no_ticket" },
            },
            description = descripcion,
        });
        using var respuesta = await EnviarAsync(HttpMethod.Post, "/v1/orders", accessToken, cuerpo, idempotencyKey, cancellationToken);
        return ParsearOrden(await LeerJsonAsync(respuesta, cancellationToken));
    }

    public async Task<OrdenMp> ObtenerOrdenAsync(string accessToken, string orderId, CancellationToken cancellationToken = default)
    {
        ExigirOrderId(orderId);
        using var respuesta = await EnviarAsync(HttpMethod.Get, $"/v1/orders/{Uri.EscapeDataString(orderId)}", accessToken, null, null, cancellationToken);
        return ParsearOrden(await LeerJsonAsync(respuesta, cancellationToken));
    }

    public async Task<OrdenMp> CancelarOrdenAsync(string accessToken, string idempotencyKey, string orderId, bool enTerminal, CancellationToken cancellationToken = default)
    {
        ExigirOrderId(orderId);
        var headers = enTerminal
            ? new Dictionary<string, string> { ["x-allow-cancelable-status"] = "at_terminal" }
            : null;
        using var respuesta = await EnviarAsync(HttpMethod.Post, $"/v1/orders/{Uri.EscapeDataString(orderId)}/cancel", accessToken, "{}", idempotencyKey, cancellationToken, headers);
        return ParsearOrden(await LeerJsonAsync(respuesta, cancellationToken));
    }

    public async Task<OrdenMp> ReembolsarOrdenAsync(string accessToken, string idempotencyKey, string orderId, long? importeMinor, CancellationToken cancellationToken = default)
    {
        ExigirOrderId(orderId);
        var cuerpo = importeMinor.HasValue
            ? JsonSerializer.Serialize(new { amount = ConvertidorMontosMp.ATexto(importeMinor.Value) })
            : "{}";
        using var respuesta = await EnviarAsync(HttpMethod.Post, $"/v1/orders/{Uri.EscapeDataString(orderId)}/refund", accessToken, cuerpo, idempotencyKey, cancellationToken);
        return ParsearOrden(await LeerJsonAsync(respuesta, cancellationToken));
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, string token, string? cuerpo, string? idempotencyKey, CancellationToken ct, Dictionary<string, string>? headers = null)
    {
        using var mensaje = new HttpRequestMessage(metodo, url);
        mensaje.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        mensaje.Headers.UserAgent.ParseAdd("LocalInfantil/0.9.0");
        if (idempotencyKey is not null)
            mensaje.Headers.Add("X-Idempotency-Key", idempotencyKey);
        if (headers is not null)
            foreach (var h in headers)
                mensaje.Headers.Add(h.Key, h.Value);
        if (cuerpo is not null)
            mensaje.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(mensaje, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            throw;
        }

        if (!respuesta.IsSuccessStatusCode)
        {
            var codigo = await ExtraerCodigoErrorAsync(respuesta, ct);
            throw MapearError(respuesta.StatusCode, codigo);
        }

        return respuesta;
    }

    private static async Task<JsonDocument> LeerJsonAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            await using var stream = await respuesta.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch
        {
            throw new ExcepcionMercadoPago("respuesta_invalida", "Mercado Pago devolvió una respuesta inválida. Reintentá en unos minutos.", null);
        }
    }

    private static OrdenMp ParsearOrden(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var id) || !root.TryGetProperty("status", out var status))
            throw new ExcepcionMercadoPago("respuesta_invalida", "Mercado Pago devolvió una respuesta inválida. Reintentá en unos minutos.", null);
        var pagos = new List<PagoOrdenMp>();
        if (root.TryGetProperty("transactions", out var tx) && tx.TryGetProperty("payments", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                pagos.Add(new PagoOrdenMp(
                    p.TryGetProperty("id", out var pid) ? pid.GetString() ?? string.Empty : string.Empty,
                    p.TryGetProperty("status", out var ps) ? ps.GetString() ?? string.Empty : string.Empty,
                    p.TryGetProperty("amount", out var pa) ? pa.GetString() ?? string.Empty : string.Empty));
            }
        }

        return new OrdenMp(
            id.GetString() ?? string.Empty,
            status.GetString() ?? EstadosOrdenMp.Desconocido,
            root.TryGetProperty("status_detail", out var sd) ? sd.GetString() : null,
            root.TryGetProperty("external_reference", out var er) ? er.GetString() : null,
            pagos);
    }

    private static async Task<string?> ExtraerCodigoErrorAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        try
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            if (texto.Length > 8192) return null;
            using var doc = JsonDocument.Parse(texto);
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString();
            if (doc.RootElement.TryGetProperty("code", out var c))
                return c.GetString();
            if (doc.RootElement.TryGetProperty("cause", out var cause) && cause.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cause.EnumerateArray())
                {
                    if (item.TryGetProperty("code", out var cc))
                        return cc.GetString();
                }
            }
        }
        catch { }
        return null;
    }

    private static ExcepcionMercadoPago MapearError(HttpStatusCode status, string? codigo) => (status, codigo) switch
    {
        (_, "already_queued_order_for_terminal") => new ExcepcionMercadoPago(codigo!, "La terminal ya tiene un cobro pendiente.", (int)status),
        (_, "idempotency_key_already_used") => new ExcepcionMercadoPago(codigo!, "La operación ya fue registrada. Verificá su estado antes de reintentar.", (int)status),
        (_, "terminal_not_allowed_action") => new ExcepcionMercadoPago(codigo!, "Mercado Pago no permite cambiar automáticamente el modo de esta terminal. Revisá la configuración del Point.", (int)status),
        (_, "order_already_canceled") => new ExcepcionMercadoPago(codigo!, "El cobro ya estaba cancelado en Mercado Pago.", (int)status),
        (_, "cannot_cancel_order") => new ExcepcionMercadoPago(codigo!, "Mercado Pago no permite cancelar en el estado actual. Verificá el estado del cobro.", (int)status),
        (HttpStatusCode.Unauthorized, _) => new ExcepcionMercadoPago(codigo ?? "unauthorized", "Credencial de Mercado Pago inválida o vencida.", (int)status),
        (HttpStatusCode.Forbidden, _) => new ExcepcionMercadoPago(codigo ?? "forbidden", "Sin permiso en Mercado Pago: la terminal o la acción no pertenecen a esta cuenta.", (int)status),
        (HttpStatusCode.NotFound, _) => new ExcepcionMercadoPago(codigo ?? "not_found", "La operación no se encontró en Mercado Pago.", (int)status),
        (HttpStatusCode.TooManyRequests, _) => new ExcepcionMercadoPago(codigo ?? "rate_limit", "Demasiadas consultas a Mercado Pago. Esperá un momento y reintentá.", (int)status),
        (HttpStatusCode.BadRequest, _) => new ExcepcionMercadoPago(codigo ?? "bad_request", "Mercado Pago rechazó la solicitud por datos inválidos. No se generó ningún cobro; revisá los datos.", (int)status),
        (HttpStatusCode.PreconditionFailed, _) => new ExcepcionMercadoPago(codigo ?? "precondition_failed", "La operación no se puede realizar en el estado actual. Verificá el estado del cobro antes de reintentar, sin asumir resultado.", (int)status),
        _ when (int)status >= 500 => new ExcepcionMercadoPago(codigo ?? "server_error", "Mercado Pago no respondió de forma concluyente. Verificá el estado antes de reintentar.", (int)status),
        _ => new ExcepcionMercadoPago(codigo ?? "request_error", "Mercado Pago rechazó la solicitud. Revisá los datos e intentá de nuevo.", (int)status),
    };

    private static void ExigirOrderId(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
            throw new ArgumentException("Falta el identificador de la order.", nameof(orderId));
    }
}
