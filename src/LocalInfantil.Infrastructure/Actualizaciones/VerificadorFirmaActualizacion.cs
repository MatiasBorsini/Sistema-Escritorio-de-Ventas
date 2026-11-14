using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Actualizaciones;

/// <summary>Verificación Ed25519 detached vía libsodium (Sodium.Core).</summary>
public sealed class VerificadorFirmaActualizacion : IVerificadorFirmaActualizacion
{
    public bool Verificar(ReadOnlySpan<byte> firma, ReadOnlySpan<byte> mensaje, ReadOnlySpan<byte> clavePublica)
    {
        if (firma.Length != 64 || clavePublica.Length != 32)
            return false;
        try
        {
            return Sodium.PublicKeyAuth.VerifyDetached(
                firma.ToArray(), mensaje.ToArray(), clavePublica.ToArray());
        }
        catch
        {
            return false;
        }
    }
}
