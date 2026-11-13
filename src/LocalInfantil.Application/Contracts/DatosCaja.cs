namespace LocalInfantil.Application.Contracts;

public static class MediosCaja
{
    public const string Efectivo = "efectivo";
    public const string Transferencia = "transferencia";
    public const string Debito = "debito";
    public const string Credito = "credito";
    public const string Billetera = "billetera";
}

public static class TiposOperacionCaja
{
    public const string Apertura = "apertura";
    public const string Cierre = "cierre";
    public const string Venta = "venta";
    public const string Anulacion = "anulacion";
    public const string MovimientoManual = "movimiento_manual";
    public const string CorreccionMedio = "correccion_medio";
    public const string Reintegro = "reintegro";
}

public static class TiposMovimientoCaja
{
    public const string Venta = "venta";
    public const string Ingreso = "ingreso";
    public const string Egreso = "egreso";
    public const string Reintegro = "reintegro";
    public const string Correccion = "correccion";
    public const string CambioCobro = "cambio_cobro";
    public const string CambioReintegro = "cambio_reintegro";
    public const string Abono = "abono";
}

public static class CategoriasMovimientoCaja
{
    public const string CambioFondo = "cambio_fondo";
    public const string OtroIngreso = "otro_ingreso";
    public const string Gasto = "gasto";
    public const string RetiroPropietario = "retiro_propietario";
    public const string OtroEgreso = "otro_egreso";

    public static readonly IReadOnlyList<string> Ingresos = [CambioFondo, OtroIngreso];
    public static readonly IReadOnlyList<string> Egresos = [Gasto, RetiroPropietario, OtroEgreso];
}

public sealed record NuevaOperacionCaja(string OperacionId, string SesionId, string Tipo, string UsuarioId, string CreadoEn);

public sealed record NuevoMovimientoCaja(
    string SesionId,
    string CajaOperacionId,
    string Tipo,
    long ImporteMinor,
    string? VentaId,
    string? PagoId,
    string? Medio,
    string? Categoria,
    string? Motivo,
    string? UsuarioId,
    string? MovimientoOrigenId,
    string CreadoEn,
    string? CambioPagoId = null,
    string? AbonoComponenteId = null);

public sealed record SesionCajaRegistrada(
    string Id,
    string Estado,
    long FondoInicialMinor,
    string Moneda,
    string AbiertaEn,
    string UsuarioAperturaId,
    string? UsuarioAperturaNombre,
    string? CerradaEn,
    string? UsuarioCierreId,
    string? UsuarioCierreNombre,
    long? EfectivoEsperadoMinor,
    long? EfectivoContadoMinor,
    long? DiferenciaMinor,
    string? ObservacionCierre,
    string OperacionAperturaId,
    string? OperacionCierreId);

public sealed record MovimientoCajaRegistrado(
    string Id,
    string Tipo,
    long ImporteMinor,
    string? VentaId,
    string? NumeroVenta,
    string? PagoId,
    string? Medio,
    string? Categoria,
    string? Motivo,
    string? UsuarioNombre,
    string? MovimientoOrigenId,
    string? OperacionId,
    string CreadoEn);

public sealed record ImportePorMedioCaja(string Medio, long ImporteMinor);

public sealed record ImportePorTipoCaja(string Tipo, string? Medio, long ImporteMinor);

public sealed record ResumenSesionCaja(SesionCajaRegistrada Sesion, long EfectivoEsperadoMinor);

public sealed record DetalleSesionCaja(
    SesionCajaRegistrada Sesion,
    long? EfectivoEsperadoMinor,
    IReadOnlyList<ImportePorMedioCaja> ResumenMedios,
    IReadOnlyList<ImportePorTipoCaja> ResumenMovimientos,
    IReadOnlyList<MovimientoCajaRegistrado> Movimientos);

public sealed record PagoCajaActual(
    string PagoId,
    string VentaId,
    string NumeroVenta,
    string EstadoVenta,
    string MedioOriginal,
    string MedioActual,
    long ImporteMinor,
    bool Corregido,
    string? MovimientoOriginalId,
    string? SesionOriginalId = null,
    string? SesionOriginalAbiertaEn = null,
    string? UsuarioSesionOriginal = null);

public sealed record VentaCorreccionSugerida(
    string VentaId,
    string Numero,
    string CreadaEn,
    long TotalMinor,
    string MediosPago,
    bool TienePagosCorregibles,
    bool SoloPoint);

public sealed record ResultadoCierreCaja(
    string SesionId,
    string OperacionId,
    long EfectivoEsperadoMinor,
    long EfectivoContadoMinor,
    long DiferenciaMinor,
    string CerradaEn);

public sealed record ArqueoCaja(string SesionId, long EfectivoContadoMinor, long EfectivoEsperadoMinor, long DiferenciaMinor);
