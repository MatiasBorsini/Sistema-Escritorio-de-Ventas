using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>Canal de distribución: obtiene el manifiesto como texto.</summary>
public interface ICanalActualizaciones
{
    Task<string> DescargarManifiestoAsync(string url, CancellationToken cancellationToken = default);
}

/// <summary>Descarga binaria con progreso y cancelación.</summary>
public interface IDescargadorActualizaciones
{
    Task DescargarAsync(string url, string destinoParcial, IProgress<long>? progreso, CancellationToken cancellationToken = default);
}

/// <summary>Verificación Ed25519 detached de la firma del manifiesto.</summary>
public interface IVerificadorFirmaActualizacion
{
    bool Verificar(ReadOnlySpan<byte> firma, ReadOnlySpan<byte> mensaje, ReadOnlySpan<byte> clavePublica);
}

/// <summary>
/// Persistencia del updater fuera de la DB comercial (archivos bajo
/// Updates/): configuración, staging (.partial), listos e instalador N-1.
/// </summary>
public interface IAlmacenActualizaciones
{
    ConfigActualizacion LeerConfiguracion();
    void GuardarConfiguracion(ConfigActualizacion config);
    string RutaStaging(string nombreArchivo);
    string RutaListo(string nombreArchivo);
    void PromoverAListo(string rutaParcial, string nombreArchivo);
    void LimpiarParciales();
    IReadOnlyList<string> InstaladoresListos();
    IReadOnlyList<ActualizacionListaPersistida> LeerActualizacionesListas();
    void GuardarManifiestoListo(string rutaInstalador, string manifiestoJson);
    string? InstaladorAnterior();
    void ConservarAnterior(string rutaInstaladorActual);
    bool HayOperacionRespaldoEnCurso();
    void RecuperarAlIniciar();
}

/// <summary>Coordinación de comprobar/descargar/verificar/instalar.</summary>
public interface ICoordinadorActualizaciones
{
    Task<EstadoActualizacion> ComprobarAsync(string? idSesion, IProgress<long>? progreso, CancellationToken cancellationToken = default);
    Task<EstadoActualizacion> DescargarAsync(string? idSesion, IProgress<long>? progreso, CancellationToken cancellationToken = default);
    Task<ResultadoInstalacionSegura> AdquirirInstalacionSeguraAsync(string? idSesion, CancellationToken cancellationToken = default);
    ManifiestoActualizacion? UltimoManifiesto { get; }
    string? UltimoError { get; }
    string? UltimaComprobacionUtc { get; }
}
