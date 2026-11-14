namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Manifiesto de actualización tal como viaja en el canal (JSON). La firma
/// cubre los campos funcionales concatenados (ver FirmadorManifiesto), no el
/// JSON completo, para no depender de canonicalización.
/// </summary>
public sealed record ManifiestoActualizacion(
    int FormatoManifiesto,
    string Version,
    string UrlDescarga,
    string Sha256,
    long TamanoBytes,
    string PublicadoEn,
    string VersionMinimaSoportada,
    string NotasVersion,
    string Firma);

/// <summary>Configuración del canal, fuera de la DB comercial.</summary>
public sealed record ConfigActualizacion(
    string? UrlBase,
    string? ClavePublicaBase64,
    string? UltimaComprobacionUtc);

public sealed record ActualizacionListaPersistida(string RutaInstalador, string ManifiestoJson);

/// <summary>Estado visible del updater. Sin instalación silenciosa.</summary>
public enum EstadoActualizacion
{
    Actualizado,
    DeshabilitadasEnDesarrollo,
    Buscando,
    CanalNoConfigurado,
    ActualizacionDisponible,
    Descargando,
    ListaParaInstalar,
    InstalarDespuesDeCerrarCaja,
    SinConexion,
    ErrorActualizacion,
}

/// <summary>Resultado de la puerta de momento seguro.</summary>
public sealed record ResultadoInstalacionSegura(bool Permitido, string? Motivo, string? RutaInstalador, IAsyncDisposable? Lease);
