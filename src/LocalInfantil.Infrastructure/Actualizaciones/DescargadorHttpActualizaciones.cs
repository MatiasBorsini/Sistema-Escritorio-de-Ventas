using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Actualizaciones;

/// <summary>
/// Descarga binaria por streaming a .partial con progreso y cancelación.
/// La cancelación o el error borran el parcial (nada a medio hacer).
/// </summary>
public sealed class DescargadorHttpActualizaciones : IDescargadorActualizaciones
{
    private readonly HttpClient _http;

    public DescargadorHttpActualizaciones(HttpClient http)
    {
        _http = http;
    }

    public async Task DescargarAsync(string url, string destinoParcial, IProgress<long>? progreso, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(destinoParcial);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        try
        {
            using var respuesta = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            respuesta.EnsureSuccessStatusCode();
            if (respuesta.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
                throw new HttpRequestException("La descarga no terminó por HTTPS.");
            await using var red = await respuesta.Content.ReadAsStreamAsync(cancellationToken);
            await using var archivo = new FileStream(destinoParcial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            int leidos;
            while ((leidos = await red.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await archivo.WriteAsync(buffer.AsMemory(0, leidos), cancellationToken);
                total += leidos;
                progreso?.Report(total);
            }
        }
        catch
        {
            try { if (File.Exists(destinoParcial)) File.Delete(destinoParcial); } catch { }
            throw;
        }
    }
}
