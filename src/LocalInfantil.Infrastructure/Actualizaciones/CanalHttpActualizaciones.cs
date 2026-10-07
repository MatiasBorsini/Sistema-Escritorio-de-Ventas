using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Actualizaciones;

/// <summary>Canal HTTP(S) de manifiestos. El HttpClient se inyecta (tests
/// usan un handler falso; producción usa timeouts por defecto).</summary>
public sealed class CanalHttpActualizaciones : ICanalActualizaciones
{
    private readonly HttpClient _http;

    public CanalHttpActualizaciones(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> DescargarManifiestoAsync(string url, CancellationToken cancellationToken = default)
    {
        using var respuesta = await _http.GetAsync(url, cancellationToken);
        respuesta.EnsureSuccessStatusCode();
        var texto = await respuesta.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 1024 * 1024)
            throw new InvalidOperationException("El manifiesto recibido es inválido.");
        return texto;
    }
}
