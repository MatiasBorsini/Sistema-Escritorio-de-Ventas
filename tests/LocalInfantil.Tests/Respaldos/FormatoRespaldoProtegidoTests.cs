using System.Security.Cryptography;
using System.Buffers.Binary;
using LocalInfantil.Infrastructure.Persistence.Respaldos;

namespace LocalInfantil.Tests.Respaldos;

public sealed class FormatoRespaldoProtegidoTests
{
    [Fact]
    public async Task Formato_streaming_roundtrip_y_autentica_todos_los_chunks()
    {
        var crypto = new FormatoRespaldoProtegido(); var key = RandomNumberGenerator.GetBytes(32);
        var clear = RandomNumberGenerator.GetBytes(5 * 1024 * 1024 + 123); var metadata = new FormatoRespaldoProtegido.MetadataRespaldo("id-1",DateTimeOffset.UtcNow.ToString("O"),"Manual",1,"test",Convert.ToHexString(SHA256.HashData(clear)),14,clear.Length,0);
        using var encrypted = new MemoryStream(); await crypto.CrearAsync(new MemoryStream(clear),encrypted,key,metadata,CancellationToken.None);
        encrypted.Position=0; using var output=new MemoryStream(); var read=await crypto.DescifrarAsync(encrypted,output,key,CancellationToken.None);
        Assert.Equal(metadata,read); Assert.Equal(clear,output.ToArray());
        var bytes=encrypted.ToArray();var offset=8+1+24;var chunk=0;var modified=false;
        while(offset+4<=bytes.Length)
        {
            var length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset,4));Assert.InRange(length,17,64*1024+17);
            if(chunk==2){bytes[offset+4+length/2]^=0x80;modified=true;break;}
            offset+=4+length;chunk++;
        }
        Assert.True(modified,"El fixture debe producir suficientes chunks para modificar uno intermedio.");using var corrupted=new MemoryStream(bytes);await Assert.ThrowsAnyAsync<Exception>(()=>crypto.DescifrarAsync(corrupted,new MemoryStream(),key,CancellationToken.None));
    }

    [Fact]
    public async Task Formato_rechaza_clave_incorrecta_y_truncamiento()
    {
        var crypto=new FormatoRespaldoProtegido();var key=RandomNumberGenerator.GetBytes(32);var meta=new FormatoRespaldoProtegido.MetadataRespaldo("id",DateTimeOffset.UtcNow.ToString("O"),"CierreCaja",1,"test",Convert.ToHexString(SHA256.HashData("x"u8)),14,1,0);
        using var archive=new MemoryStream();await crypto.CrearAsync(new MemoryStream("x"u8.ToArray()),archive,key,meta,CancellationToken.None);
        var bytes=archive.ToArray();using var wrong=new MemoryStream(bytes);await Assert.ThrowsAnyAsync<Exception>(()=>crypto.DescifrarAsync(wrong,new MemoryStream(),RandomNumberGenerator.GetBytes(32),CancellationToken.None));
        using var cut=new MemoryStream(bytes[..^5]);await Assert.ThrowsAnyAsync<Exception>(()=>crypto.DescifrarAsync(cut,new MemoryStream(),key,CancellationToken.None));
        var unknown=(byte[])bytes.Clone();unknown[8]=255;using var unsupported=new MemoryStream(unknown);await Assert.ThrowsAsync<InvalidDataException>(()=>crypto.DescifrarAsync(unsupported,new MemoryStream(),key,CancellationToken.None));
        var appended=bytes.Concat(new byte[]{1}).ToArray();using var trailing=new MemoryStream(appended);await Assert.ThrowsAsync<InvalidDataException>(()=>crypto.DescifrarAsync(trailing,new MemoryStream(),key,CancellationToken.None));
    }

    [Fact]
    public void Codigo_recuperacion_codifica_valida_checksum_y_detecta_transcripcion()
    {
        var key=RandomNumberGenerator.GetBytes(32);var code=CodigoRecuperacion.Codificar(key);var changed=(code[0]=='A'?'B':'A')+code[1..];Assert.True(CodigoRecuperacion.Validar(code,key));Assert.False(CodigoRecuperacion.Validar(changed,key));Assert.Equal(key,CodigoRecuperacion.Decodificar(code));
    }
}
