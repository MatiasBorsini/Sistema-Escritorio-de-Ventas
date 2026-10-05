using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

/// <summary>OAuth Desktop PKCE + REST Drive v3 restringido a drive.file. No contiene client secret.</summary>
[SupportedOSPlatform("windows")]
public sealed class ClienteGoogleDriveRespaldos : IDriveRespaldos
{
    public const string Scope="https://www.googleapis.com/auth/drive.file";
    private readonly string _secrets; private readonly HttpClient _http; private readonly Func<string,Task> _abrirNavegador;private readonly IProtectorSecretos _protector;
    private const string TokenEntropy="LocalInfantil.GoogleDrive.Token.v1";
    private string? _folderId;
    public ClienteGoogleDriveRespaldos(string directorioSecretos,HttpClient? http=null,Func<string,Task>? abrirNavegador=null,IProtectorSecretos? protector=null)
    { _secrets=Path.GetFullPath(directorioSecretos); _http=http??new HttpClient{Timeout=TimeSpan.FromMinutes(3)};_abrirNavegador=abrirNavegador??AbrirNavegadorAsync;_protector=protector??ProtectorDpapi.Instancia; }
    public bool Configurado=>!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LOCALINFANTIL_GOOGLE_CLIENT_ID"));
    public bool Conectado=>File.Exists(Path.Combine(_secrets,"google-token.dpapi"))&&!File.Exists(Path.Combine(_secrets,"google-reconnect-required.flag"));

    public async Task ConectarAsync(CancellationToken ct=default)
    {
        var clientId=Environment.GetEnvironmentVariable("LOCALINFANTIL_GOOGLE_CLIENT_ID");if(string.IsNullOrWhiteSpace(clientId))throw new InvalidOperationException("Google Drive todavía no está configurado. Definí LOCALINFANTIL_GOOGLE_CLIENT_ID con el OAuth Desktop Client ID.");
        using var probe=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);probe.Start();var port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
        var redirect=$"http://127.0.0.1:{port}/";using var listener=new HttpListener();listener.Prefixes.Add(redirect);listener.Start();
        var verifier=Base64Url(RandomNumberGenerator.GetBytes(32));var challenge=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));var state=Base64Url(RandomNumberGenerator.GetBytes(24));
        var auth=new UriBuilder("https://accounts.google.com/o/oauth2/v2/auth"){Query=BuildQuery(new Dictionary<string,string>{{"client_id",clientId},{"redirect_uri",redirect},{"response_type","code"},{"scope",Scope},{"access_type","offline"},{"prompt","consent"},{"code_challenge",challenge},{"code_challenge_method","S256"},{"state",state}})}.Uri;
        await _abrirNavegador(auth.ToString());using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);linked.CancelAfter(TimeSpan.FromMinutes(3));var context=await listener.GetContextAsync().WaitAsync(linked.Token);
        var code=context.Request.QueryString["code"];var returned=context.Request.QueryString["state"];var error=context.Request.QueryString["error"];
        var page=Encoding.UTF8.GetBytes("<html><meta charset=utf-8><title>LocalInfantil</title><p>Autorización recibida. Podés cerrar esta ventana.</p></html>");context.Response.ContentType="text/html; charset=utf-8";context.Response.ContentLength64=page.Length;await context.Response.OutputStream.WriteAsync(page,ct);context.Response.Close();
        if(!string.IsNullOrEmpty(error)||string.IsNullOrWhiteSpace(code)||!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state),Encoding.UTF8.GetBytes(returned??"")))throw new InvalidOperationException("Google canceló o rechazó la conexión.");
        using var response=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",clientId},{"code",code},{"code_verifier",verifier},{"redirect_uri",redirect},{"grant_type","authorization_code"}}),ct);var json=await response.Content.ReadAsStringAsync(ct);response.EnsureSuccessStatusCode();
        var token=JsonSerializer.Deserialize<TokenGoogle>(json)??throw new InvalidDataException("Respuesta OAuth inválida.");if(string.IsNullOrWhiteSpace(token.RefreshToken))throw new InvalidOperationException("Google no entregó refresh token; desconectá la aplicación en Google y volvé a autorizar.");SaveToken(token);
    }

    public async Task<string> ReservarIdArchivoAsync(string backupId,CancellationToken ct=default)
    {
        var token=await AccessTokenAsync(ct);using var req=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/drive/v3/files/generateIds?count=1");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,ct);var body=await res.Content.ReadAsStringAsync(ct);res.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(body);return doc.RootElement.GetProperty("ids")[0].GetString()!;
    }

    public async Task<string> SubirAsync(ManifiestoRespaldo manifest,CancellationToken ct=default)
    {
        if(string.IsNullOrWhiteSpace(manifest.RemoteReservedId))throw new InvalidOperationException("Primero reservá y guardá el ID remoto en el manifiesto.");
        var token=await AccessTokenAsync(ct);var folder=await FolderIdAsync(token,ct);
        var existing=await ObtenerArchivoAsync(token,manifest.RemoteReservedId,ct);
        if(existing is not null){await ConfirmarAsync(existing.Value,manifest,ct);return manifest.RemoteReservedId;}
        var metadata=JsonSerializer.SerializeToUtf8Bytes(new {id=manifest.RemoteReservedId,name=Path.GetFileName(manifest.RutaLocal),mimeType="application/octet-stream",parents=new[]{folder},appProperties=new Dictionary<string,string>{{"app","LocalInfantil"},{"backup_id",manifest.BackupId},{"sha256",manifest.Sha256},{"created_utc",manifest.CreadoUtc},{"reason",manifest.Motivo}}});
        using var init=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/upload/drive/v3/files?uploadType=resumable&fields=id,size,md5Checksum,appProperties");init.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);init.Content=new ByteArrayContent(metadata);init.Content.Headers.ContentType=new MediaTypeHeaderValue("application/json; charset=UTF-8");
        using var started=await _http.SendAsync(init,ct);started.EnsureSuccessStatusCode();var location=started.Headers.Location??throw new InvalidDataException("Drive no devolvió la ubicación de subida.");
        await using var file=File.OpenRead(manifest.RutaLocal);using var content=new StreamContent(file,128*1024);content.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");content.Headers.ContentLength=file.Length;using var upload=new HttpRequestMessage(HttpMethod.Put,location){Content=content};upload.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var done=await _http.SendAsync(upload,HttpCompletionOption.ResponseHeadersRead,ct);var json=await done.Content.ReadAsStringAsync(ct);done.EnsureSuccessStatusCode();using var result=JsonDocument.Parse(json);var id=result.RootElement.GetProperty("id").GetString()!;if(!string.Equals(id,manifest.RemoteReservedId,StringComparison.Ordinal))throw new InvalidDataException("Drive devolvió un ID distinto del reservado; no se confirma la copia.");await ConfirmarAsync(result.RootElement,manifest,ct);return id;
    }

    public async Task<IReadOnlyList<ArchivoDrive>> ListarAsync(CancellationToken ct=default)
    {
        var token=await AccessTokenAsync(ct);var folder=await FolderIdAsync(token,ct);var all=new List<ArchivoDrive>();string? page=null;
        do{var query=new Dictionary<string,string>{{"q",$"'{folder}' in parents and trashed = false"},{"fields","nextPageToken,files(id,name,size,createdTime,md5Checksum,appProperties)"},{"pageSize","1000"}};if(page is not null)query["pageToken"]=page;var uri="https://www.googleapis.com/drive/v3/files?"+BuildQuery(query);using var req=new HttpRequestMessage(HttpMethod.Get,uri);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,ct);var body=await res.Content.ReadAsStringAsync(ct);res.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(body);foreach(var e in doc.RootElement.GetProperty("files").EnumerateArray()){if(!e.TryGetProperty("appProperties",out var props)||!props.TryGetProperty("app",out var app)||app.GetString()!="LocalInfantil")continue;var created=props.TryGetProperty("created_utc",out var backupDate)&&DateTimeOffset.TryParse(backupDate.GetString(),out var parsedDate)?parsedDate:e.GetProperty("createdTime").GetDateTimeOffset();all.Add(new(e.GetProperty("id").GetString()!,e.GetProperty("name").GetString()!,e.TryGetProperty("size",out var size)?long.Parse(size.GetString()!):0,created,props.TryGetProperty("reason",out var reason)?reason.GetString():null,props.TryGetProperty("backup_id",out var backupId)?backupId.GetString():null));}page=doc.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString():null;}while(page is not null);
        return all;
    }

    public async Task DescargarAsync(string fileId,string destino,CancellationToken ct=default)
    {var token=await AccessTokenAsync(ct);using var req=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}?alt=media");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);res.EnsureSuccessStatusCode();await using var source=await res.Content.ReadAsStreamAsync(ct);await using var target=new FileStream(destino,FileMode.CreateNew,FileAccess.Write,FileShare.None,128*1024,FileOptions.Asynchronous|FileOptions.WriteThrough);await source.CopyToAsync(target,128*1024,ct);await target.FlushAsync(ct);}

    public async Task EliminarAsync(string fileId,CancellationToken ct=default)
    {var token=await AccessTokenAsync(ct);using var req=new HttpRequestMessage(HttpMethod.Delete,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(fileId)}");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,ct);if(res.StatusCode!=HttpStatusCode.NotFound)res.EnsureSuccessStatusCode();}

    private async Task<string> FolderIdAsync(string token,CancellationToken ct)
    {
        var stored=Path.Combine(_secrets,"drive-folder-id.txt");if(_folderId is not null)return _folderId;if(File.Exists(stored))return _folderId=(await File.ReadAllTextAsync(stored,ct)).Trim();
        var q="name = 'LocalInfantil Backups' and mimeType = 'application/vnd.google-apps.folder' and trashed = false and appProperties has { key='app' and value='LocalInfantil' }";var uri="https://www.googleapis.com/drive/v3/files?"+BuildQuery(new Dictionary<string,string>{{"q",q},{"fields","files(id)"},{"pageSize","100"}});using var req=new HttpRequestMessage(HttpMethod.Get,uri);req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,ct);var body=await res.Content.ReadAsStringAsync(ct);res.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(body);var files=doc.RootElement.GetProperty("files");if(files.GetArrayLength()>1)throw new InvalidDataException("Hay más de una carpeta administrada LocalInfantil; revisá Drive antes de continuar.");if(files.GetArrayLength()==1){var found=files[0].GetProperty("id").GetString()!;await GuardarFolderIdAsync(stored,found,ct);return _folderId=found;}
        var payload=JsonSerializer.Serialize(new{name="LocalInfantil Backups",mimeType="application/vnd.google-apps.folder",appProperties=new Dictionary<string,string>{{"app","LocalInfantil"}}});using var create=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/drive/v3/files?fields=id");create.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);create.Content=new StringContent(payload,Encoding.UTF8,"application/json");using var made=await _http.SendAsync(create,ct);var response=await made.Content.ReadAsStringAsync(ct);made.EnsureSuccessStatusCode();using var created=JsonDocument.Parse(response);var folder=created.RootElement.GetProperty("id").GetString()!;await GuardarFolderIdAsync(stored,folder,ct);return _folderId=folder;
    }
    private static async Task GuardarFolderIdAsync(string path,string id,CancellationToken ct){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var tmp=path+".tmp";await File.WriteAllTextAsync(tmp,id,ct);if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
    private async Task<JsonElement?> ObtenerArchivoAsync(string token,string id,CancellationToken ct)
    {using var req=new HttpRequestMessage(HttpMethod.Get,$"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?fields=id,size,md5Checksum,appProperties");req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);using var res=await _http.SendAsync(req,ct);if(res.StatusCode==HttpStatusCode.NotFound)return null;var body=await res.Content.ReadAsStringAsync(ct);res.EnsureSuccessStatusCode();using var doc=JsonDocument.Parse(body);return doc.RootElement.Clone();}
    private static async Task ConfirmarAsync(JsonElement remote,ManifiestoRespaldo manifest,CancellationToken ct){if(!remote.TryGetProperty("appProperties",out var props)||!props.TryGetProperty("backup_id",out var id)||id.GetString()!=manifest.BackupId||!props.TryGetProperty("sha256",out var sha)||sha.GetString()!=manifest.Sha256)throw new InvalidDataException("El archivo remoto no coincide con el manifiesto local.");if(!remote.TryGetProperty("size",out var size)||long.Parse(size.GetString()!)!=new FileInfo(manifest.RutaLocal).Length)throw new InvalidDataException("El tamaño remoto no coincide con el paquete local.");if(remote.TryGetProperty("md5Checksum",out var md5)){await using var local=File.OpenRead(manifest.RutaLocal);var localMd5=Convert.ToHexString(await MD5.HashDataAsync(local,ct)).ToLowerInvariant();if(!string.Equals(md5.GetString(),localMd5,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("La verificación del contenido remoto no coincide.");}}
    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        var token=LoadToken();if(token.ExpiresAtUtc>DateTimeOffset.UtcNow.AddMinutes(2))return token.AccessToken;if(string.IsNullOrWhiteSpace(token.RefreshToken)){MarcarReconexion();throw new InvalidOperationException("Volver a conectar Google Drive.");}var clientId=Environment.GetEnvironmentVariable("LOCALINFANTIL_GOOGLE_CLIENT_ID");if(string.IsNullOrWhiteSpace(clientId))throw new InvalidOperationException("Google Drive todavía no está configurado.");using var res=await _http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"client_id",clientId},{"refresh_token",token.RefreshToken},{"grant_type","refresh_token"}}),ct);var body=await res.Content.ReadAsStringAsync(ct);if(res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest){MarcarReconexion();throw new InvalidOperationException("Volver a conectar Google Drive.");}res.EnsureSuccessStatusCode();var refreshed=JsonSerializer.Deserialize<TokenGoogle>(body)!;token=refreshed with {RefreshToken=refreshed.RefreshToken??token.RefreshToken,ExpiresAtUtc=DateTimeOffset.UtcNow.AddSeconds(refreshed.ExpiresIn)};SaveToken(token);return token.AccessToken;
    }
    private TokenGoogle LoadToken(){var path=Path.Combine(_secrets,"google-token.dpapi");if(!File.Exists(path))throw new InvalidOperationException("Conectá Google Drive para subir copias pendientes.");var clear=_protector.Desproteger(File.ReadAllBytes(path),Encoding.UTF8.GetBytes(TokenEntropy));try{return JsonSerializer.Deserialize<TokenGoogle>(clear)??throw new InvalidDataException("Token local inválido.");}finally{CryptographicOperations.ZeroMemory(clear);}}
    private void SaveToken(TokenGoogle token){Directory.CreateDirectory(_secrets);var clear=JsonSerializer.SerializeToUtf8Bytes(token);try{var protectedToken=_protector.Proteger(clear,Encoding.UTF8.GetBytes(TokenEntropy));var path=Path.Combine(_secrets,"google-token.dpapi");var temp=path+".tmp";File.WriteAllBytes(temp,protectedToken);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);var marker=Path.Combine(_secrets,"google-reconnect-required.flag");if(File.Exists(marker))File.Delete(marker);}finally{CryptographicOperations.ZeroMemory(clear);}}
    private void MarcarReconexion(){Directory.CreateDirectory(_secrets);File.WriteAllText(Path.Combine(_secrets,"google-reconnect-required.flag"),"reconnect");}
    private static string BuildQuery(IEnumerable<KeyValuePair<string,string>> query)=>string.Join("&",query.Select(x=>Uri.EscapeDataString(x.Key)+"="+Uri.EscapeDataString(x.Value)));
    private static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static Task AbrirNavegadorAsync(string url){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});return Task.CompletedTask;}
    public void Dispose()=>_http.Dispose();
    private sealed record TokenGoogle([property:JsonPropertyName("access_token")] string AccessToken,[property:JsonPropertyName("expires_in")] int ExpiresIn,[property:JsonPropertyName("refresh_token")] string? RefreshToken,[property:JsonPropertyName("token_type")] string TokenType="Bearer") { [JsonPropertyName("expires_at_utc")] public DateTimeOffset ExpiresAtUtc {get;init;}=DateTimeOffset.UtcNow.AddSeconds(ExpiresIn); }
    public sealed record ArchivoDrive(string Id,string Nombre,long TamanoBytes,DateTimeOffset CreadoUtc,string? Motivo,string? BackupId);
}
