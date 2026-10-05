using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

/// <summary>Contenedor streaming versionado: magic/version/header SecretStream públicos; payload y metadata cifrados.</summary>
public sealed class FormatoRespaldoProtegido
{
    private static readonly byte[] Magic = "LIBACKUP"u8.ToArray();
    private const byte Version = 1;
    private const int ChunkBytes = 64 * 1024, HeaderBytes = 24, KeyBytes = 32, OverheadBytes = 17;

    public async Task CrearAsync(Stream sqlite, Stream destino, ReadOnlyMemory<byte> clave, MetadataRespaldo metadata, CancellationToken ct)
    {
        if (clave.Length != KeyBytes) throw new ArgumentException("La clave de respaldo no tiene el tamaño esperado.", nameof(clave));
        Sodium.Init(); var header = new byte[HeaderBytes]; var state = new byte[Sodium.StateBytes()];var key=clave.ToArray();
        try{Sodium.InitPush(state, header, key);}finally{CryptographicOperations.ZeroMemory(key);}
        await destino.WriteAsync(Magic, ct); await destino.WriteAsync(new[] { Version }, ct); await destino.WriteAsync(header, ct);
        await using (var encrypted = new CipherWriteStream(destino, state))
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(metadata); var size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, json.Length);
            await encrypted.WriteAsync(size, ct); await encrypted.WriteAsync(json, ct);
            await using var gzip = new GZipStream(encrypted, CompressionLevel.Optimal, leaveOpen: true);
            await sqlite.CopyToAsync(gzip, ChunkBytes, ct);
        }
        CryptographicOperations.ZeroMemory(state);
    }

    public async Task CrearDesdeGZipAsync(Stream gzip,Stream destino,ReadOnlyMemory<byte> clave,MetadataRespaldo metadata,CancellationToken ct)
    {
        if(!gzip.CanRead)throw new ArgumentException("El flujo comprimido no es legible.",nameof(gzip));
        if(clave.Length!=KeyBytes)throw new ArgumentException("La clave de respaldo no tiene el tamaño esperado.",nameof(clave));
        Sodium.Init();var header=new byte[HeaderBytes];var state=new byte[Sodium.StateBytes()];var key=clave.ToArray();try{Sodium.InitPush(state,header,key);}finally{CryptographicOperations.ZeroMemory(key);}
        await destino.WriteAsync(Magic,ct);await destino.WriteAsync(new[]{Version},ct);await destino.WriteAsync(header,ct);
        try
        {
            await using var encrypted=new CipherWriteStream(destino,state);
            var json=JsonSerializer.SerializeToUtf8Bytes(metadata);var size=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(size,json.Length);
            await encrypted.WriteAsync(size,ct);await encrypted.WriteAsync(json,ct);await gzip.CopyToAsync(encrypted,ChunkBytes,ct);
        }
        finally{CryptographicOperations.ZeroMemory(state);}
    }

    public static MetadataRespaldo ConTamanoPaquete(MetadataRespaldo metadata,long tamanoComprimidoBytes)
    {
        var result=metadata with{TamanoComprimidoBytes=tamanoComprimidoBytes,TamanoPaqueteBytes=0};
        for(var i=0;i<8;i++)
        {
            var jsonLength=JsonSerializer.SerializeToUtf8Bytes(result).Length;
            var secretBytes=checked(4L+jsonLength+tamanoComprimidoBytes);
            var chunks=checked(secretBytes/ChunkBytes+1);
            var size=checked(Magic.Length+1+HeaderBytes+secretBytes+chunks*(4+OverheadBytes));
            if(result.TamanoPaqueteBytes==size)return result;
            result=result with{TamanoPaqueteBytes=size};
        }
        throw new InvalidOperationException("No se pudo calcular el tamaño del paquete.");
    }

    public async Task<MetadataRespaldo> DescifrarAsync(Stream origen, Stream destino, ReadOnlyMemory<byte> clave, CancellationToken ct)
    {
        if (clave.Length != KeyBytes) throw new InvalidDataException("El código de recuperación no tiene el formato esperado.");
        Sodium.Init(); var magic = new byte[Magic.Length]; await origen.ReadExactlyAsync(magic, ct);
        if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("El archivo no es un respaldo LocalInfantil.");
        if (origen.ReadByte() != Version) throw new InvalidDataException("La versión del respaldo no es compatible.");
        var header = new byte[HeaderBytes]; await origen.ReadExactlyAsync(header, ct); var state = new byte[Sodium.StateBytes()];var key=clave.ToArray();try{Sodium.InitPull(state, header, key);}finally{CryptographicOperations.ZeroMemory(key);}
        await using var decrypted = new CipherReadStream(origen, state);
        var lengthBytes = new byte[4]; await decrypted.ReadExactlyAsync(lengthBytes, ct); var n = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (n is < 2 or > 64 * 1024) throw new InvalidDataException("Metadata de respaldo inválida.");
        var json = new byte[n]; await decrypted.ReadExactlyAsync(json, ct);
        var metadata = JsonSerializer.Deserialize<MetadataRespaldo>(json) ?? throw new InvalidDataException("Metadata de respaldo inválida.");
        if(metadata.Formato!=Version||metadata.TamanoOriginalBytes<0||metadata.TamanoPaqueteBytes<0||metadata.TamanoComprimidoBytes<0)throw new InvalidDataException("Los datos protegidos del respaldo no son compatibles.");
        await using (var gzip = new GZipStream(decrypted, CompressionMode.Decompress, leaveOpen: true)) await gzip.CopyToAsync(destino, ChunkBytes, ct);
        if (!decrypted.FinalVisto) throw new InvalidDataException("El respaldo está truncado.");
        if(origen.ReadByte()!=-1)throw new InvalidDataException("El respaldo contiene datos agregados después del cierre autenticado.");
        CryptographicOperations.ZeroMemory(state); return metadata;
    }

    public sealed record MetadataRespaldo(string BackupId, string CreadoUtc, string Motivo, int Formato, string VersionApp, string Sha256BaseDatos, int VersionEsquema, long TamanoOriginalBytes, long TamanoPaqueteBytes,string? OperacionOrigen=null,long TamanoComprimidoBytes=0);

    private sealed class CipherWriteStream(Stream inner, byte[] state) : Stream
    {
        private readonly byte[] _buffer = new byte[ChunkBytes]; private int _count; private bool _closed;
        public override bool CanRead=>false; public override bool CanSeek=>false; public override bool CanWrite=>true; public override long Length=>throw new NotSupportedException(); public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush()=>inner.Flush(); public override Task FlushAsync(CancellationToken ct)=>inner.FlushAsync(ct);
        public override void Write(byte[] b,int o,int c)=>WriteAsync(b.AsMemory(o,c)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> src,CancellationToken ct=default) { while(!src.IsEmpty) { var n=Math.Min(ChunkBytes-_count,src.Length); src[..n].CopyTo(_buffer.AsMemory(_count)); _count+=n; src=src[n..]; if(_count==ChunkBytes) await EmitAsync(false,ct); } }
        private async Task EmitAsync(bool final,CancellationToken ct) { var cipher=new byte[_count+OverheadBytes]; Sodium.Push(state,cipher,out var len,_buffer,(ulong)_count,final?(byte)3:(byte)0); var frame=new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(frame,checked((int)len)); await inner.WriteAsync(frame,ct); await inner.WriteAsync(cipher.AsMemory(0,(int)len),ct); CryptographicOperations.ZeroMemory(_buffer); _count=0; }
        protected override void Dispose(bool disposing) { if(disposing&&!_closed) { EmitAsync(true,CancellationToken.None).GetAwaiter().GetResult(); _closed=true; CryptographicOperations.ZeroMemory(state); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { if(!_closed) { await EmitAsync(true,CancellationToken.None); _closed=true; CryptographicOperations.ZeroMemory(state); } await base.DisposeAsync(); }
        public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException(); public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException(); public override void SetLength(long v)=>throw new NotSupportedException();
    }

    private sealed class CipherReadStream(Stream inner, byte[] state) : Stream
    {
        private byte[] _clear=Array.Empty<byte>(); private int _offset; private bool _end;
        public bool FinalVisto {get;private set;} public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false; public override long Length=>throw new NotSupportedException(); public override long Position {get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> output,CancellationToken ct=default)
        {
            if(output.IsEmpty)return 0;
            while(_offset==_clear.Length) { if(_end)return 0; var frame=new byte[4]; var got=await ReadFrameAsync(inner,frame,ct); if(got==0){_end=true;return 0;} if(got!=4)throw new InvalidDataException("Chunk cifrado incompleto."); var n=BinaryPrimitives.ReadInt32LittleEndian(frame); if(n<OverheadBytes||n>ChunkBytes+OverheadBytes)throw new InvalidDataException("Chunk cifrado inválido."); var cipher=new byte[n]; await inner.ReadExactlyAsync(cipher,ct); var clear=new byte[n-OverheadBytes]; Sodium.Pull(state,clear,out var actual,out var tag,cipher,(ulong)n); _clear=clear.AsSpan(0,checked((int)actual)).ToArray(); _offset=0; if(tag==3){FinalVisto=true;_end=true;} }
            var copied=Math.Min(output.Length,_clear.Length-_offset); _clear.AsMemory(_offset,copied).CopyTo(output); _offset+=copied; return copied;
        }
        private static async Task<int> ReadFrameAsync(Stream s,Memory<byte> b,CancellationToken ct){var n=0;while(n<b.Length){var r=await s.ReadAsync(b[n..],ct);if(r==0)break;n+=r;}return n;}
        public override int Read(byte[] b,int o,int c)=>ReadAsync(b.AsMemory(o,c)).AsTask().GetAwaiter().GetResult();
        public override int Read(Span<byte> b){var temp=new byte[b.Length];var n=Read(temp,0,temp.Length);temp.AsSpan(0,n).CopyTo(b);return n;}
        protected override void Dispose(bool disposing){if(disposing)CryptographicOperations.ZeroMemory(state);base.Dispose(disposing);} public override void Flush(){} public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException(); public override void SetLength(long v)=>throw new NotSupportedException(); public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }

    private static class Sodium
    {
        private const string Lib="libsodium";
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="sodium_init")]static extern int InitNative();
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="crypto_secretstream_xchacha20poly1305_statebytes")]static extern UIntPtr StateBytesNative();
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="crypto_secretstream_xchacha20poly1305_init_push")]static extern int InitPushNative(byte[] state,byte[] header,byte[] key);
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="crypto_secretstream_xchacha20poly1305_init_pull")]static extern int InitPullNative(byte[] state,byte[] header,byte[] key);
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="crypto_secretstream_xchacha20poly1305_push")]static extern int PushNative(byte[] state,byte[] cipher,out ulong clen,byte[] clear,ulong clearLen,IntPtr ad,ulong adLen,byte tag);
        [DllImport(Lib,CallingConvention=CallingConvention.Cdecl,EntryPoint="crypto_secretstream_xchacha20poly1305_pull")]static extern int PullNative(byte[] state,byte[] clear,out ulong clearLen,out byte tag,byte[] cipher,ulong cipherLen,IntPtr ad,ulong adLen);
        public static void Init(){if(InitNative()<0)throw new CryptographicException("No se pudo inicializar libsodium.");} public static int StateBytes()=>checked((int)StateBytesNative().ToUInt64());
        public static void InitPush(byte[] s,byte[] h,byte[] k){if(InitPushNative(s,h,k)!=0)throw new CryptographicException("No se pudo iniciar el cifrado.");} public static void InitPull(byte[] s,byte[] h,byte[] k){if(InitPullNative(s,h,k)!=0)throw new CryptographicException("Clave incorrecta o encabezado inválido.");}
        public static void Push(byte[] s,byte[] c,out ulong n,byte[] m,ulong ml,byte t){if(PushNative(s,c,out n,m,ml,IntPtr.Zero,0,t)!=0)throw new CryptographicException("Falló el cifrado autenticado.");} public static void Pull(byte[] s,byte[] m,out ulong n,out byte t,byte[] c,ulong cl){if(PullNative(s,m,out n,out t,c,cl,IntPtr.Zero,0)!=0)throw new CryptographicException("El respaldo está dañado o la clave es incorrecta.");}
    }
}
