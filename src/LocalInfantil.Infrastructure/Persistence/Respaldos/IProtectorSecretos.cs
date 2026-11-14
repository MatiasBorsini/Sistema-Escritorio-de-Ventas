using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

/// <summary>Frontera testeable para proteger material secreto ligado al perfil Windows actual.</summary>
public interface IProtectorSecretos
{
    byte[] Proteger(ReadOnlySpan<byte> secreto,ReadOnlySpan<byte> entropia);
    byte[] Desproteger(ReadOnlySpan<byte> protegido,ReadOnlySpan<byte> entropia);
}

[SupportedOSPlatform("windows")]
public sealed class ProtectorDpapi:IProtectorSecretos
{
    public static ProtectorDpapi Instancia{get;}=new();
    private ProtectorDpapi(){}
    public byte[] Proteger(ReadOnlySpan<byte> secreto,ReadOnlySpan<byte> entropia)
    {
        var clear=secreto.ToArray();var entropy=entropia.ToArray();try{return ProtectedData.Protect(clear,entropy,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(clear);CryptographicOperations.ZeroMemory(entropy);}
    }
    public byte[] Desproteger(ReadOnlySpan<byte> protegido,ReadOnlySpan<byte> entropia)
    {
        var cipher=protegido.ToArray();var entropy=entropia.ToArray();try{return ProtectedData.Unprotect(cipher,entropy,DataProtectionScope.CurrentUser);}finally{CryptographicOperations.ZeroMemory(cipher);CryptographicOperations.ZeroMemory(entropy);}
    }
}
