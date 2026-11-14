using System.Net;
using System.Text;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>Request capturada (sin secretos: nunca se registra Authorization).</summary>
public sealed record RequestCapturada(string Metodo, string Url, string? Cuerpo, string? IdempotencyKey);

/// <summary>
/// HttpMessageHandler fake programable para la suite F12 (sin Internet).
/// Responde según (método, path) y captura requests para asserts.
/// </summary>
public sealed class FakeMpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, string?, HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RequestCapturada> _requests = new();
    private readonly object _lock = new();

    public IReadOnlyList<RequestCapturada> Requests
    {
        get { lock (_lock) return _requests.ToList(); }
    }

    public void Responder(string metodo, string pathPrefijo, Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        => _rutas[$"{metodo} {pathPrefijo}"] = responder;

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Error(HttpStatusCode status, string codigo)
        => Json(status, $"{{\"error\":\"{codigo}\",\"message\":\"fake\",\"status\":{(int)status}}}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        string? key = null;
        if (request.Headers.TryGetValues("X-Idempotency-Key", out var keys))
            key = keys.FirstOrDefault();
        lock (_lock)
            _requests.Add(new RequestCapturada(request.Method.Method, request.RequestUri?.AbsolutePath ?? string.Empty, cuerpo, key));

        foreach (var (ruta, responder) in _rutas.OrderByDescending(r => r.Key.Length))
        {
            var partes = ruta.Split(' ', 2);
            if (request.Method.Method.Equals(partes[0], StringComparison.OrdinalIgnoreCase)
                && (request.RequestUri?.AbsolutePath ?? string.Empty).StartsWith(partes[1], StringComparison.OrdinalIgnoreCase))
                return responder(request, cuerpo);
        }

        return Error(HttpStatusCode.NotFound, "not_found");
    }
}
