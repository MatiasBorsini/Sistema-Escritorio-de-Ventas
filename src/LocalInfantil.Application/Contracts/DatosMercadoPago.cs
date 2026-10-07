namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Terminal Point devuelta por GET /terminals/v1/list. La compatibilidad
/// se decide SOLO por ModoOperativo (PDV integra por API); el Id puede
/// contener el modelo como información, nunca como condición lógica.
/// </summary>
public sealed record TerminalPoint(
    string Id,
    string? PosId,
    string? SucursalId,
    string? PosExternoId,
    string ModoOperativo);

/// <summary>Página de terminales con paginación oficial (limit/offset).</summary>
public sealed record PaginaTerminales(
    IReadOnlyList<TerminalPoint> Terminales,
    long Total,
    int Limite,
    int Desplazamiento);

/// <summary>Estados remotos de una order (GET /v1/orders/{id}).</summary>
public static class EstadosOrdenMp
{
    public const string Created = "created";
    public const string AtTerminal = "at_terminal";
    public const string Processed = "processed";
    public const string Failed = "failed";
    public const string Refunded = "refunded";
    public const string Expired = "expired";
    public const string Canceled = "canceled";
    public const string Desconocido = "desconocido";
}

/// <summary>Estados locales de la saga (mercado_pago_operacion.estado_local).</summary>
public static class EstadosLocalesMp
{
    public const string Preparada = "preparada";
    public const string CreandoOrder = "creando_order";
    public const string EsperandoTerminal = "esperando_terminal";
    public const string ProcesadaRemota = "procesada_remota";
    public const string ConfirmandoLocal = "confirmando_local";
    public const string Confirmada = "confirmada";
    public const string Fallida = "fallida";
    public const string Cancelada = "cancelada";
    public const string Expirada = "expirada";
    public const string RequiereConciliacion = "requiere_conciliacion";
}

/// <summary>Estados locales del reembolso (mercado_pago_reembolso.estado_local).</summary>
public static class EstadosLocalesReembolsoMp
{
    public const string Solicitada = "solicitada";
    public const string ProcesadaRemota = "procesada_remota";
    public const string ConfirmadaLocal = "confirmada_local";
    public const string Fallida = "fallida";
    public const string RequiereConciliacion = "requiere_conciliacion";
}

/// <summary>Order Point tal como la devuelve la API (subconjunto usado).</summary>
public sealed record OrdenMp(
    string Id,
    string Estado,
    string? DetalleEstado,
    string? ReferenciaExterna,
    IReadOnlyList<PagoOrdenMp> Pagos);

public sealed record PagoOrdenMp(string Id, string Estado, string Importe);

/// <summary>Configuración NO secreta (Secrets/mercadopago.json).</summary>
public sealed record ConfigMercadoPago(
    string? TerminalId,
    string? ModoTerminal,
    string? SucursalId,
    string? PosId,
    string? ConectadoEn,
    string? UltimaComprobacionUtc);

/// <summary>Operación saga persistida (lectura).</summary>
public sealed record OperacionMpRegistrada(
    string Id,
    string OperacionId,
    string IdempotencyKey,
    string ExternalReference,
    string? OrderId,
    string TerminalId,
    long ImporteMinor,
    string EstadoLocal,
    string? EstadoMp,
    string? VentaId,
    string CajaSesionId,
    string? UltimoError,
    string CreadaEn,
    string ActualizadaEn);

/// <summary>Reembolso persistido (lectura).</summary>
public sealed record ReembolsoMpRegistrado(
    string Id,
    string? OperacionMpId,
    string VentaId,
    string PagoId,
    string ReembolsoOperacionId,
    string IdempotencyKey,
    string OrderId,
    long ImporteMinor,
    string EstadoLocal,
    string? EstadoMp,
    string? UltimoError);
