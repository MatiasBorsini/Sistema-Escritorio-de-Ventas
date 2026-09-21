using System.Security.Cryptography;
using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Seguridad;

/// <summary>
/// Huellas PBKDF2-HMAC-SHA512 con sal aleatoria de 16 bytes, 220000
/// iteraciones (referencia OWASP indicada por el proyecto) y subclave de
/// 32 bytes. Comparación en tiempo constante. Formato versionado
/// "v1$iteraciones$sal$huella" (Base64): permite subir parámetros o algoritmo
/// por etapas re-generando la huella al iniciar sesión cuando
/// NecesitaActualizacion lo indique.
/// Política mínima (propuesta documentada, sin clases de caracteres
/// arbitrarias y admitiendo frases): entre 12 y 256 caracteres.
/// </summary>
public sealed class Pbkdf2HashContrasenas : IHashContrasenas
{
    private const string VersionActual = "v1";
    private const int Iteraciones = 220_000;
    private const int BytesSal = 16;
    private const int BytesHash = 32;
    private const int LongitudMinima = 12;
    private const int LongitudMaxima = 256;

    public string CrearHuella(string contrasena)
    {
        ValidarPolitica(contrasena);
        var sal = RandomNumberGenerator.GetBytes(BytesSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, Iteraciones, HashAlgorithmName.SHA512, BytesHash);
        return $"{VersionActual}${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string contrasena, string? huella)
    {
        if (string.IsNullOrEmpty(contrasena) || string.IsNullOrEmpty(huella))
        {
            return false;
        }

        var partes = huella.Split('$');
        if (partes.Length != 4 || partes[0] != VersionActual)
        {
            return false;
        }

        // Tope: un conteo manipulado por encima del vigente no se ejecuta
        // (evita consumo desproporcionado); esa huella queda inválida.
        if (!int.TryParse(partes[1], out var iteraciones) || iteraciones <= 0 || iteraciones > Iteraciones)
        {
            return false;
        }

        byte[] sal;
        byte[] esperada;
        try
        {
            sal = Convert.FromBase64String(partes[2]);
            esperada = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (esperada.Length == 0)
        {
            return false;
        }

        var real = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA512, esperada.Length);
        return CryptographicOperations.FixedTimeEquals(real, esperada);
    }

    public bool NecesitaActualizacion(string? huella)
    {
        if (string.IsNullOrEmpty(huella))
        {
            return true;
        }

        var partes = huella.Split('$');
        return partes.Length != 4
            || partes[0] != VersionActual
            || !int.TryParse(partes[1], out var iteraciones)
            || iteraciones != Iteraciones;
    }

    private static void ValidarPolitica(string contrasena)
    {
        if (contrasena is null || contrasena.Length < LongitudMinima || contrasena.Length > LongitudMaxima)
        {
            throw new ArgumentException(
                $"La contraseña debe tener entre {LongitudMinima} y {LongitudMaxima} caracteres.",
                nameof(contrasena));
        }
    }
}
