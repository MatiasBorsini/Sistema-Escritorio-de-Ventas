using LocalInfantil.Infrastructure.MercadoPago;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §40: token fuera de SQLite/logs/config/repo, DPAPI roundtrip.</summary>
public sealed class SecretosMpTests
{
    [Fact]
    public async Task Credencial_dpapi_roundtrip_sin_exponer()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var cred = new CredencialPropiaDpapi(dir);
            Assert.False(await cred.ExisteAsync());
            await cred.GuardarAsync("APP_USR-FAKE-TOKEN-123");
            Assert.True(await cred.ExisteAsync());
            Assert.Equal("APP_USR-FAKE-TOKEN-123", await cred.ObtenerAsync());
            var plano = Path.Combine(dir, "mercadopago_token.bin");
            Assert.DoesNotContain("APP_USR-FAKE-TOKEN-123", await File.ReadAllTextAsync(plano));
            await cred.EliminarAsync();
            Assert.False(await cred.ExisteAsync());
            Assert.Null(await cred.ObtenerAsync());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Token_no_queda_en_sqlite_ni_config()
    {
        using var e = await EntornoMp.CrearAsync();
        const string token = "APP_USR-FAKE-TOKEN-XYZ";
        await e.Credencial.GuardarAsync(token);
        var bytes = await File.ReadAllBytesAsync(e.RutaDb);
        Assert.DoesNotContain(token, System.Text.Encoding.Latin1.GetString(bytes));
        var dir = Path.GetDirectoryName(e.RutaDb)!;
        foreach (var json in Directory.EnumerateFiles(dir, "*.json"))
            Assert.DoesNotContain(token, await File.ReadAllTextAsync(json));
    }

    [Fact]
    public void Errores_sanitizados_sin_token()
    {
        var ex = new Application.Abstractions.ExcepcionMercadoPago("x", "Credencial de Mercado Pago inválida o vencida.", 401);
        Assert.DoesNotContain("APP_USR", ex.Message);
        Assert.DoesNotContain("Bearer", ex.Message);
    }
}
