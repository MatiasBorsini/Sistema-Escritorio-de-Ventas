using System.Security.Cryptography;
using System.Text;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Infrastructure.Persistence.Respaldos;

namespace LocalInfantil.Infrastructure.MercadoPago;

/// <summary>
/// Implementación inicial del provider: token propio protegido con DPAPI
/// CurrentUser en Secrets/mercadopago_token.bin (nunca SQLite, nunca plano,
/// nunca repo). Preparada para un futuro OAuthCredencialProvider sin
/// cambiar el flujo F12.
/// </summary>
public sealed class CredencialPropiaDpapi : IMercadoPagoCredencialProvider
{
    private static readonly byte[] Entropia = Encoding.UTF8.GetBytes("LocalInfantil.MercadoPago.v1");
    private readonly string _rutaToken;
    private readonly IProtectorSecretos _protector;

    public CredencialPropiaDpapi(string dirSecretos, IProtectorSecretos? protector = null)
    {
        _rutaToken = Path.Combine(dirSecretos, "mercadopago_token.bin");
        _protector = protector ?? ProtectorDpapi.Instancia;
    }

    public Task GuardarAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_rutaToken);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var claro = Encoding.UTF8.GetBytes(accessToken);
        try
        {
            var protegido = _protector.Proteger(claro, Entropia);
            var tmp = _rutaToken + ".tmp";
            File.WriteAllBytes(tmp, protegido);
            File.Move(tmp, _rutaToken, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(claro);
        }
        return Task.CompletedTask;
    }

    public Task<string?> ObtenerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_rutaToken)) return Task.FromResult<string?>(null);
            var protegido = File.ReadAllBytes(_rutaToken);
            var claro = _protector.Desproteger(protegido, Entropia);
            try
            {
                return Task.FromResult<string?>(Encoding.UTF8.GetString(claro));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(claro);
            }
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task EliminarAsync(CancellationToken cancellationToken = default)
    {
        try { if (File.Exists(_rutaToken)) File.Delete(_rutaToken); } catch { }
        try { if (File.Exists(_rutaToken + ".tmp")) File.Delete(_rutaToken + ".tmp"); } catch { }
        return Task.CompletedTask;
    }

    public Task<bool> ExisteAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(_rutaToken));
}
