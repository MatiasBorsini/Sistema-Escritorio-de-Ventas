using System.Security.Cryptography;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Actualizaciones;

/// <summary>
/// Coordinador del updater: comprobar → descargar automáticamente en segundo
/// plano → verificar → instalación manual confirmada. Sin instalación
/// silenciosa. La
/// versión instalada se inyecta (WPF la lee del assembly) para no acoplar
/// Application a WPF.
/// </summary>
public sealed class CoordinadorActualizaciones : ICoordinadorActualizaciones
{
    private const int FormatoSoportado = 1;

    private readonly ICanalActualizaciones _canal;
    private readonly IDescargadorActualizaciones _descargador;
    private readonly IVerificadorFirmaActualizacion _verificador;
    private readonly IAlmacenActualizaciones _almacen;
    private readonly PuertaInstalacion _puerta;
    private readonly IAutorizador _autorizador;
    private readonly string _versionInstalada;
    private readonly IReloj _reloj;
    private readonly bool _permitirHttpLocal;

    private ManifiestoActualizacion? _manifiesto;
    private string? _instaladorListo;
    private bool _descargando;

    public CoordinadorActualizaciones(
        ICanalActualizaciones canal,
        IDescargadorActualizaciones descargador,
        IVerificadorFirmaActualizacion verificador,
        IAlmacenActualizaciones almacen,
        PuertaInstalacion puerta,
        IAutorizador autorizador,
        string versionInstalada,
        IReloj reloj,
        bool permitirHttpLocal = false)
    {
        _canal = canal;
        _descargador = descargador;
        _verificador = verificador;
        _almacen = almacen;
        _puerta = puerta;
        _autorizador = autorizador;
        _versionInstalada = versionInstalada;
        _reloj = reloj;
        _permitirHttpLocal = permitirHttpLocal;
        _almacen.RecuperarAlIniciar();
        RehidratarActualizacionLista();
    }

    public ManifiestoActualizacion? UltimoManifiesto => _manifiesto;
    public string? UltimoError { get; private set; }
    public string? UltimaComprobacionUtc => _almacen.LeerConfiguracion().UltimaComprobacionUtc;

    /// <summary>
    /// Formato binario versionado: dominio + campos UTF-8 precedidos por
    /// longitud Int32 big-endian. Firma todos los datos lógicos del manifiesto.
    /// </summary>
    public static byte[] ConstruirMensajeFirmado(ManifiestoActualizacion manifiesto)
    {
        using var salida = new MemoryStream();
        var dominio = Encoding.UTF8.GetBytes("LocalInfantil.UpdateManifest\0canonical-v1");
        salida.Write(dominio);
        EscribirCampo(salida, manifiesto.FormatoManifiesto.ToString(System.Globalization.CultureInfo.InvariantCulture));
        EscribirCampo(salida, manifiesto.Version);
        EscribirCampo(salida, manifiesto.UrlDescarga);
        EscribirCampo(salida, manifiesto.Sha256.ToUpperInvariant());
        EscribirCampo(salida, manifiesto.TamanoBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        EscribirCampo(salida, manifiesto.PublicadoEn);
        EscribirCampo(salida, manifiesto.VersionMinimaSoportada);
        EscribirCampo(salida, manifiesto.NotasVersion);
        return salida.ToArray();
    }

    private static void EscribirCampo(Stream salida, string valor)
    {
        var bytes = Encoding.UTF8.GetBytes(valor ?? string.Empty);
        Span<byte> longitud = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(longitud, bytes.Length);
        salida.Write(longitud);
        salida.Write(bytes);
    }

    public static bool IntentarParsearVersion(string? texto, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var partes = texto.Trim().Split('.');
        if (partes.Length != 3) return false;
        if (!partes.All(p => p.Length > 0 && p.All(char.IsDigit))) return false;
        try
        {
            version = new Version(int.Parse(partes[0]), int.Parse(partes[1]), int.Parse(partes[2]));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<EstadoActualizacion> ComprobarAsync(string? idSesion, IProgress<long>? progreso, CancellationToken cancellationToken = default)
    {
        UltimoError = null;
        var config = _almacen.LeerConfiguracion();
        if (string.IsNullOrWhiteSpace(config.UrlBase) || string.IsNullOrWhiteSpace(config.ClavePublicaBase64))
            return EstadoListoSiSigueVigente() ?? EstadoActualizacion.CanalNoConfigurado;

        string texto;
        try
        {
            texto = await _canal.DescargarManifiestoAsync(config.UrlBase!, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return EstadoListoSiSigueVigente() ?? EstadoActualizacion.SinConexion;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            UltimoError = "El servidor no respondió a tiempo. Reintentá en unos minutos.";
            return EstadoListoSiSigueVigente() ?? EstadoActualizacion.ErrorActualizacion;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            UltimoError = $"No se pudo comprobar: {ex.Message}";
            return EstadoListoSiSigueVigente() ?? EstadoActualizacion.ErrorActualizacion;
        }

        ManifiestoActualizacion manifiesto;
        try
        {
            manifiesto = ValidarManifiesto(texto, config);
        }
        catch (InvalidOperationException ex)
        {
            UltimoError = ex.Message;
            return EstadoListoSiSigueVigente() ?? EstadoActualizacion.ErrorActualizacion;
        }

        if (!IntentarParsearVersion(_versionInstalada, out var instalada))
        {
            UltimoError = "La versión instalada es inválida.";
            return EstadoActualizacion.ErrorActualizacion;
        }

        _manifiesto = manifiesto;
        _almacen.GuardarConfiguracion(config with { UltimaComprobacionUtc = _reloj.UtcNow().ToString("o") });

        if (!IntentarParsearVersion(manifiesto.Version, out var remota))
        {
            UltimoError = "Versión de actualización inválida.";
            return EstadoActualizacion.ErrorActualizacion;
        }

        if (remota <= instalada)
            return EstadoActualizacion.Actualizado;

        if (!IntentarParsearVersion(manifiesto.VersionMinimaSoportada, out var minima) || instalada < minima)
        {
            UltimoError = $"La versión {manifiesto.Version} requiere instalación manual en este equipo.";
            return EstadoActualizacion.ErrorActualizacion;
        }

        if (_instaladorListo is not null && InstaladorSigueVigente(manifiesto))
            return EstadoActualizacion.ListaParaInstalar;

        return await DescargarAsync(idSesion, progreso, cancellationToken);
    }

    public async Task<EstadoActualizacion> DescargarAsync(string? idSesion, IProgress<long>? progreso, CancellationToken cancellationToken = default)
    {
        UltimoError = null;
        if (_manifiesto is null)
            return await ComprobarAsync(idSesion, progreso, cancellationToken);

        var nombre = NombreArchivoSeguro(_manifiesto.UrlDescarga);
        var parcial = _almacen.RutaStaging(nombre);
        _descargando = true;
        try
        {
            await _descargador.DescargarAsync(_manifiesto.UrlDescarga, parcial, progreso, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UltimoError = "Descarga cancelada. La versión actual sigue funcionando.";
            return EstadoActualizacion.ActualizacionDisponible;
        }
        catch (HttpRequestException)
        {
            return EstadoActualizacion.SinConexion;
        }
        catch (Exception ex)
        {
            UltimoError = $"No se pudo descargar: {ex.Message}";
            return EstadoActualizacion.ErrorActualizacion;
        }
        finally
        {
            _descargando = false;
        }

        try
        {
            VerificarIntegridad(parcial, _manifiesto);
        }
        catch (InvalidOperationException ex)
        {
            UltimoError = ex.Message;
            return EstadoActualizacion.ErrorActualizacion;
        }

        _almacen.PromoverAListo(parcial, nombre);
        var rutaLista = _almacen.RutaListo(nombre);
        _almacen.GuardarManifiestoListo(rutaLista, JsonSerializer.Serialize(_manifiesto));
        if (_instaladorListo is not null)
            _almacen.ConservarAnterior(_instaladorListo);
        _instaladorListo = rutaLista;
        return EstadoActualizacion.ListaParaInstalar;
    }

    public async Task<ResultadoInstalacionSegura> AdquirirInstalacionSeguraAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        if (_descargando)
            return new ResultadoInstalacionSegura(false, "Hay una descarga en curso. Esperá a que termine.", null, null);
        if (_instaladorListo is null || !File.Exists(_instaladorListo))
            return new ResultadoInstalacionSegura(false, "No hay instalador listo. Completá la descarga primero.", null, null);
        if (string.IsNullOrWhiteSpace(idSesion))
            return new ResultadoInstalacionSegura(false, "Se requiere una sesión Dueña autenticada.", null, null);

        if (_manifiesto is null || !InstaladorSigueVigente(_manifiesto))
            return new ResultadoInstalacionSegura(false, "El instalador listo ya no es válido; buscá la actualización nuevamente.", null, null);
        var resultado = await _puerta.AdquirirAsync(_instaladorListo, cancellationToken);
        if (!resultado.Permitido) return resultado;
        try
        {
            var usuario = await _autorizador.AutorizarAsync(idSesion, PermisosActualizaciones.Instalar, cancellationToken);
            if (usuario.Rol != RolesLocal.Duena)
                throw new AccesoDenegadoException(PermisosActualizaciones.Instalar);
            return resultado;
        }
        catch
        {
            await resultado.Lease!.DisposeAsync();
            throw;
        }
    }

    private ManifiestoActualizacion ValidarManifiesto(string texto, ConfigActualizacion config)
    {
        ManifiestoActualizacion? manifiesto;
        try
        {
            manifiesto = JsonSerializer.Deserialize<ManifiestoActualizacion>(texto,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("El manifiesto está corrupto. La versión actual sigue funcionando.");
        }

        if (manifiesto is null || manifiesto.FormatoManifiesto != FormatoSoportado)
            throw new InvalidOperationException("Manifiesto no soportado. La versión actual sigue funcionando.");

        if (!IntentarParsearVersion(manifiesto.Version, out _) ||
            !IntentarParsearVersion(manifiesto.VersionMinimaSoportada, out _))
            throw new InvalidOperationException("El manifiesto trae una versión inválida.");

        ValidarUrl(manifiesto.UrlDescarga);

        if (manifiesto.TamanoBytes <= 0)
            throw new InvalidOperationException("El manifiesto trae un tamaño inválido.");

        if (string.IsNullOrWhiteSpace(manifiesto.Sha256) || manifiesto.Sha256.Length != 64 || !manifiesto.Sha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("El manifiesto trae un hash inválido.");
        if (string.IsNullOrWhiteSpace(manifiesto.Firma))
            throw new InvalidOperationException("La firma del manifiesto está vacía.");

        byte[] clave;
        try { clave = Convert.FromBase64String(config.ClavePublicaBase64!); }
        catch { throw new InvalidOperationException("La clave pública del canal es inválida."); }

        byte[] firma;
        try { firma = Convert.FromBase64String(manifiesto.Firma); }
        catch { throw new InvalidOperationException("La firma del manifiesto es inválida."); }

        var mensaje = ConstruirMensajeFirmado(manifiesto);
        if (!_verificador.Verificar(firma, mensaje, clave))
            throw new InvalidOperationException("La firma del manifiesto no es válida. No se descargó nada.");

        return manifiesto;
    }

    private void RehidratarActualizacionLista()
    {
        var config = _almacen.LeerConfiguracion();
        if (string.IsNullOrWhiteSpace(config.ClavePublicaBase64)) return;
        foreach (var guardada in _almacen.LeerActualizacionesListas().Reverse())
        {
            try
            {
                var manifiesto = ValidarManifiesto(guardada.ManifiestoJson, config);
                if (!string.Equals(Path.GetFullPath(guardada.RutaInstalador),
                        Path.GetFullPath(_almacen.RutaListo(NombreArchivoSeguro(manifiesto.UrlDescarga))),
                        StringComparison.OrdinalIgnoreCase)) continue;
                VerificarIntegridad(guardada.RutaInstalador, manifiesto);
                _manifiesto = manifiesto;
                _instaladorListo = guardada.RutaInstalador;
                return;
            }
            catch { }
        }
    }

    private EstadoActualizacion? EstadoListoSiSigueVigente()
    {
        if (_instaladorListo is null || _manifiesto is null || !InstaladorSigueVigente(_manifiesto)) return null;
        if (!IntentarParsearVersion(_versionInstalada, out var instalada) ||
            !IntentarParsearVersion(_manifiesto.Version, out var disponible) || disponible <= instalada) return null;
        return EstadoActualizacion.ListaParaInstalar;
    }

    private void ValidarUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("El manifiesto trae una URL inválida.");

        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("El manifiesto trae una URL inválida.");
            return;
        }

        var esLocal = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("127.0.0.1") || uri.Host.Equals("::1"));
        if (esLocal && _permitirHttpLocal)
            return;

        throw new InvalidOperationException("La actualización debe descargarse por HTTPS.");
    }

    private static void VerificarIntegridad(string ruta, ManifiestoActualizacion manifiesto)
    {
        var info = new FileInfo(ruta);
        if (info.Length != manifiesto.TamanoBytes)
        {
            TryBorrar(ruta);
            throw new InvalidOperationException("La descarga llegó incompleta. Reintentá la descarga.");
        }

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(ruta);
        var hash = Convert.ToHexString(sha.ComputeHash(stream));
        if (!hash.Equals(manifiesto.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryBorrar(ruta);
            throw new InvalidOperationException("La descarga no coincide con el manifiesto. Reintentá la descarga.");
        }
    }

    private bool InstaladorSigueVigente(ManifiestoActualizacion manifiesto)
    {
        if (!File.Exists(_instaladorListo)) return false;
        try
        {
            VerificarIntegridad(_instaladorListo!, manifiesto);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string NombreArchivoSeguro(string url)
    {
        var ultimo = url.Split('/').LastOrDefault() ?? "actualizacion.bin";
        foreach (var c in Path.GetInvalidFileNameChars())
            ultimo = ultimo.Replace(c, '_');
        if (string.IsNullOrWhiteSpace(ultimo)) ultimo = "actualizacion.bin";
        return ultimo;
    }

    private static void TryBorrar(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); } catch { }
    }
}
