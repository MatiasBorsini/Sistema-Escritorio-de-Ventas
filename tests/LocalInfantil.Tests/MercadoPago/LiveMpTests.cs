using LocalInfantil.Infrastructure.MercadoPago;
using Xunit.Abstractions;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// Tests LIVE manuales: solo corren con LOCALINFANTIL_MP_TEST_ACCESS_TOKEN
/// (credencial de PRUEBA, nunca productiva). Sin variable: se omiten con
/// mensaje claro y la suite sigue en verde.
/// </summary>
public sealed class LiveMpTests
{
    private readonly ITestOutputHelper _salida;

    public LiveMpTests(ITestOutputHelper salida) => _salida = salida;

    private static string? TokenPrueba()
        => Environment.GetEnvironmentVariable("LOCALINFANTIL_MP_TEST_ACCESS_TOKEN");

    [Fact]
    public async Task Live_listar_terminales_contrato_oficial()
    {
        var token = TokenPrueba();
        if (string.IsNullOrWhiteSpace(token))
        {
            _salida.WriteLine("LIVE omitido: definí LOCALINFANTIL_MP_TEST_ACCESS_TOKEN (credencial de prueba) para verificar el contrato oficial.");
            return;
        }

        var cliente = new MercadoPagoPointClient(new HttpClient());
        var pagina = await cliente.ListarTerminalesAsync(token, 10, 0);
        Assert.NotNull(pagina.Terminales);
        foreach (var t in pagina.Terminales)
            Assert.False(string.IsNullOrWhiteSpace(t.Id));
    }
}
