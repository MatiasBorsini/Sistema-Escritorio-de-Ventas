using System.Security.Cryptography;
using System.Text.Json;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Actualizaciones;
using LocalInfantil.Infrastructure.Actualizaciones;
using LocalInfantil.Infrastructure.Tiempo;

namespace LocalInfantil.Tests.Actualizaciones;

public sealed class ManifiestoActualizacionTests
{
    [Fact]
    public void Firma_ed25519_cubre_todos_los_campos_logicos()
    {
        var par = Sodium.PublicKeyAuth.GenerateKeyPair();
        var verificador = new VerificadorFirmaActualizacion();
        var original = ManifiestoBase();
        var mensaje = CoordinadorActualizaciones.ConstruirMensajeFirmado(original);
        var firma = Sodium.PublicKeyAuth.SignDetached(mensaje, par.PrivateKey);
        Assert.True(verificador.Verificar(firma, mensaje, par.PublicKey));

        var cambios = new[]
        {
            original with { FormatoManifiesto = 2 },
            original with { Version = "0.9.2" },
            original with { UrlDescarga = "https://example.test/otro.exe" },
            original with { Sha256 = new string('A', 64) },
            original with { TamanoBytes = original.TamanoBytes + 1 },
            original with { PublicadoEn = "2026-10-08T00:00:00Z" },
            original with { VersionMinimaSoportada = "0.9.1" },
            original with { NotasVersion = "Notas alteradas" },
        };

        foreach (var alterado in cambios)
            Assert.False(verificador.Verificar(firma, CoordinadorActualizaciones.ConstruirMensajeFirmado(alterado), par.PublicKey));

        var otroPar = Sodium.PublicKeyAuth.GenerateKeyPair();
        Assert.False(verificador.Verificar(firma, mensaje, otroPar.PublicKey));
    }

    [Fact]
    public async Task Comprobar_descarga_actualizacion_en_background_y_la_rehidrata_tras_reinicio()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalInfantil-actualizaciones-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var binario = "instalador-de-prueba"u8.ToArray();
            var par = Sodium.PublicKeyAuth.GenerateKeyPair();
            var sinFirma = ManifiestoBase(binario);
            var firma = Sodium.PublicKeyAuth.SignDetached(CoordinadorActualizaciones.ConstruirMensajeFirmado(sinFirma), par.PrivateKey);
            var manifiesto = sinFirma with { Firma = Convert.ToBase64String(firma) };
            var almacen = new AlmacenActualizaciones(Path.Combine(root, "Updates"), Path.Combine(root, "locks"));
            almacen.GuardarConfiguracion(new ConfigActualizacion("https://example.test/channel.json", Convert.ToBase64String(par.PublicKey), null));
            var descargador = new DescargadorFalso(binario);
            var coordinador = CrearCoordinador(almacen, new CanalFalso(JsonSerializer.Serialize(manifiesto)), descargador);

            var estado = await coordinador.ComprobarAsync(null, null);
            Assert.Equal(EstadoActualizacion.ListaParaInstalar, estado);
            Assert.Equal(1, descargador.Llamadas);
            Assert.Single(almacen.InstaladoresListos());
            Assert.Single(almacen.LeerActualizacionesListas());

            var reiniciado = CrearCoordinador(
                new AlmacenActualizaciones(Path.Combine(root, "Updates"), Path.Combine(root, "locks")),
                new CanalFalso(string.Empty),
                new DescargadorFalso(binario));
            Assert.Equal("0.9.1", reiniciado.UltimoManifiesto?.Version);
            Assert.Equal(EstadoActualizacion.ListaParaInstalar, await reiniciado.ComprobarAsync(null, null));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static CoordinadorActualizaciones CrearCoordinador(AlmacenActualizaciones almacen, ICanalActualizaciones canal, IDescargadorActualizaciones descargador)
        => new(canal, descargador, new VerificadorFirmaActualizacion(), almacen,
            puerta: null!, autorizador: null!, versionInstalada: "0.9.0", reloj: new RelojSistema());

    private static ManifiestoActualizacion ManifiestoBase(byte[]? binario = null)
    {
        var contenido = binario ?? "contenido"u8.ToArray();
        return new ManifiestoActualizacion(1, "0.9.1", "https://example.test/LocalInfantil-Setup-0.9.1.exe",
            Convert.ToHexString(SHA256.HashData(contenido)), contenido.Length, "2026-10-07T00:00:00Z", "0.9.0", "Notas v1", "");
    }

    private sealed class CanalFalso(string texto) : ICanalActualizaciones
    {
        public Task<string> DescargarManifiestoAsync(string url, CancellationToken cancellationToken = default) => Task.FromResult(texto);
    }

    private sealed class DescargadorFalso(byte[] contenido) : IDescargadorActualizaciones
    {
        public int Llamadas { get; private set; }
        public async Task DescargarAsync(string url, string destinoParcial, IProgress<long>? progreso, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            await File.WriteAllBytesAsync(destinoParcial, contenido, cancellationToken);
            progreso?.Report(contenido.Length);
        }
    }
}
