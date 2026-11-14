using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

/// <summary>Error MP ya traducido a mensaje humano (sin secretos).</summary>
public sealed class ExcepcionMercadoPago : Exception
{
    public string Codigo { get; }
    public int? HttpStatus { get; }

    public ExcepcionMercadoPago(string codigo, string mensaje, int? httpStatus = null)
        : base(mensaje)
    {
        Codigo = codigo;
        HttpStatus = httpStatus;
    }
}

/// <summary>
/// Cliente HTTP aislado contra Orders API / Terminals API. Sin SDK.
/// El token lo provee el llamador (nunca se loguea).
/// </summary>
public interface IMercadoPagoPointClient
{
    Task<PaginaTerminales> ListarTerminalesAsync(string accessToken, int limite, int desplazamiento, CancellationToken cancellationToken = default);
    Task<TerminalPoint> ConfigurarModoAsync(string accessToken, string terminalId, string modo, CancellationToken cancellationToken = default);
    Task<OrdenMp> CrearOrdenAsync(string accessToken, string idempotencyKey, string externalReference, long importeMinor, string terminalId, string descripcion, CancellationToken cancellationToken = default);
    Task<OrdenMp> ObtenerOrdenAsync(string accessToken, string orderId, CancellationToken cancellationToken = default);
    Task<OrdenMp> CancelarOrdenAsync(string accessToken, string idempotencyKey, string orderId, bool enTerminal, CancellationToken cancellationToken = default);
    Task<OrdenMp> ReembolsarOrdenAsync(string accessToken, string idempotencyKey, string orderId, long? importeMinor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abstracción de credencial para permitir OAuth futuro sin cambiar el
/// flujo. Implementación inicial: token propio protegido con DPAPI.
/// </summary>
public interface IMercadoPagoCredencialProvider
{
    Task GuardarAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<string?> ObtenerAsync(CancellationToken cancellationToken = default);
    Task EliminarAsync(CancellationToken cancellationToken = default);
    Task<bool> ExisteAsync(CancellationToken cancellationToken = default);
}

/// <summary>Configuración NO secreta (terminal seleccionada, etc.).</summary>
public interface IAlmacenConfigMercadoPago
{
    ConfigMercadoPago Leer();
    void Guardar(ConfigMercadoPago config);
}

/// <summary>Persistencia de la saga F12 (tablas mercado_pago_*).</summary>
public interface IRepositorioMercadoPago
{
    Task<string> InsertarOperacionAsync(ISesionPersistencia sesion, string operacionId, string idempotencyKey, string externalReference, string terminalId, long importeMinor, string cajaSesionId, string usuarioId, string creadaEn, CancellationToken cancellationToken = default);
    Task InsertarLineaAsync(ISesionPersistencia sesion, string operacionMpId, string varianteId, string codigoInterno, string nombreProducto, string? talle, string? color, long cantidad, long precioUnitarioMinor, CancellationToken cancellationToken = default);
    Task InsertarPagoPlanificadoAsync(ISesionPersistencia sesion, string operacionMpId, string medio, long importeMinor, long? recibidoMinor, CancellationToken cancellationToken = default);
    Task<OperacionMpRegistrada?> ObtenerOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
    Task<OperacionMpRegistrada?> ObtenerOperacionPorOrderAsync(ISesionPersistencia sesion, string orderId, CancellationToken cancellationToken = default);
    Task<OperacionMpRegistrada?> ObtenerOperacionPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosPorPagoAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperacionMpRegistrada>> ListarNoTerminalesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperacionMpRegistrada>> ListarPendientesPorSesionAsync(ISesionPersistencia sesion, string cajaSesionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string VarianteId, long Cantidad, long PrecioUnitarioMinor, string CodigoInterno, string NombreProducto, string? Talle, string? Color)>> ListarLineasAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Medio, long ImporteMinor, long? RecibidoMinor)>> ListarPagosPlanificadosAsync(ISesionPersistencia sesion, string operacionMpId, CancellationToken cancellationToken = default);
    Task ActualizarEstadoAsync(ISesionPersistencia sesion, string id, string estadoLocal, string? estadoMp, string? orderId, string? ventaId, string? ultimoError, string actualizadaEn, CancellationToken cancellationToken = default);
    Task<string> InsertarReembolsoAsync(ISesionPersistencia sesion, string? operacionMpId, string ventaId, string pagoId, string reembolsoOperacionId, string idempotencyKey, string orderId, long importeMinor, string creadoEn, CancellationToken cancellationToken = default);
    Task<ReembolsoMpRegistrado?> ObtenerReembolsoAsync(ISesionPersistencia sesion, string reembolsoOperacionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReembolsoMpRegistrado>> ListarReembolsosNoTerminalesAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default);
    Task ActualizarReembolsoAsync(ISesionPersistencia sesion, string id, string estadoLocal, string? estadoMp, string? ultimoError, string actualizadoEn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra un cobro manual Point. Debe llamarse DENTRO de la transacción
    /// que crea la venta y el pago (atómico): si falla, no queda confirmación
    /// parcial.
    /// </summary>
    Task<string> InsertarCobroManualAsync(ISesionPersistencia sesion, string operacionId, string ventaId, string pagoId, long importeMinor, string? comprobanteRef, string? terminalId, string? posId, string declaradoEn, string estadoConciliacion, string usuarioId, string cajaSesionId, CancellationToken cancellationToken = default);
    Task<bool> ExisteCobroManualPorOperacionAsync(ISesionPersistencia sesion, string operacionId, CancellationToken cancellationToken = default);
    Task<CobroManualMpRegistrado?> ObtenerCobroManualPorVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobros manuales declarados con el mismo importe (y la misma terminal si
    /// se especifica) dentro de la ventana reciente, EXCLUYENDO los ya
    /// conciliados. Sirve solo para advertir y exigir confirmación humana: no
    /// descarta ni vincula nada automáticamente.
    /// </summary>
    Task<IReadOnlyList<CobroManualMpRegistrado>> ListarCobrosManualesSimilaresAsync(ISesionPersistencia sesion, long importeMinor, string? terminalId, string desdeDeclaradoEn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Indica si una referencia de comprobante ya fue declarada en otro cobro
    /// manual. Un comprobante identifica un único cobro físico: no debe quedar
    /// vinculado a dos operaciones distintas.
    /// </summary>
    Task<bool> ExisteCobroManualPorComprobanteAsync(ISesionPersistencia sesion, string comprobanteRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cobro manual declarado con esa referencia de comprobante. Se usa para la
    /// conciliación asistida: un cobro del reporte puede identificarse por el
    /// comprobante que la empleada declaró. null si ninguno coincide.
    /// </summary>
    Task<CobroManualMpRegistrado?> ObtenerCobroManualPorComprobanteAsync(ISesionPersistencia sesion, string comprobanteRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Actualiza el estado de conciliación de un cobro manual (los datos base
    /// siguen inmutables). Lo usa la conciliación con el reporte.
    /// </summary>
    Task ActualizarEstadoConciliacionCobroManualAsync(ISesionPersistencia sesion, string cobroManualId, string estadoConciliacion, string actualizadoEn, CancellationToken cancellationToken = default);
}
