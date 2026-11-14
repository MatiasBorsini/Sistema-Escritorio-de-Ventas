using System.Security.Cryptography;
using System.Text.Json;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Actualizaciones;

if (args.Length != 8)
{
    Console.Error.WriteLine("Uso: ManifestSigner <instalador> <version> <url-https> <publicado-utc> <version-minima> <notas> <clave-privada-base64-fuera-del-repo> <directorio-salida>");
    return 2;
}

var installer = Path.GetFullPath(args[0]);
var version = args[1];
var downloadUrl = args[2];
var publishedAt = args[3];
var minimumVersion = args[4];
var notes = args[5];
var keyPath = Path.GetFullPath(args[6]);
var output = Path.GetFullPath(args[7]);
if (!File.Exists(installer)) throw new FileNotFoundException("No se encuentra el instalador.", installer);
if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
    throw new InvalidOperationException("El instalador debe publicarse con URL HTTPS.");

var key = Convert.FromBase64String(await File.ReadAllTextAsync(keyPath));
try
{
    if (key.Length != 64) throw new InvalidOperationException("La clave privada Ed25519 debe contener 64 bytes en Base64.");
    var info = new FileInfo(installer);
    var manifest = new ManifiestoActualizacion(1, version, downloadUrl, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installer))), info.Length,
        publishedAt, minimumVersion, notes, string.Empty);
    var payload = CoordinadorActualizaciones.ConstruirMensajeFirmado(manifest);
    var signature = Sodium.PublicKeyAuth.SignDetached(payload, key);
    manifest = manifest with { Firma = Convert.ToBase64String(signature) };
    Directory.CreateDirectory(output);
    await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(Path.Combine(output, "manifest.sig"), Convert.ToBase64String(signature));
    Console.WriteLine($"Manifiesto creado para versión {version}; tamaño {info.Length} bytes; SHA-256 {manifest.Sha256}.");
    return 0;
}
finally
{
    CryptographicOperations.ZeroMemory(key);
}
