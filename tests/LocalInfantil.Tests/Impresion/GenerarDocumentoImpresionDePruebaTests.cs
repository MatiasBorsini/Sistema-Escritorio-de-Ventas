using LocalInfantil.Application.Features.Impresion;

namespace LocalInfantil.Tests.Impresion;

public sealed class GenerarDocumentoImpresionDePruebaTests
{
    [Fact]
    public void Genera_contenido_ficticio_con_total_calculado()
    {
        var reloj = new FakeTimeProvider(new DateTimeOffset(2026, 9, 20, 15, 30, 0, TimeSpan.Zero));
        var generador = new GenerarDocumentoImpresionDePrueba(reloj);

        var documento = generador.Generar();

        Assert.Equal("Local Infantil — documento de prueba", documento.NombreLocal);
        Assert.Equal(3, documento.Lineas.Count);
        Assert.Equal(53_500m, documento.Total);
        Assert.All(documento.Lineas, linea => Assert.Contains("ejemplo", linea.Descripcion));
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _ahora;

        public FakeTimeProvider(DateTimeOffset ahora)
        {
            _ahora = ahora;
        }

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
