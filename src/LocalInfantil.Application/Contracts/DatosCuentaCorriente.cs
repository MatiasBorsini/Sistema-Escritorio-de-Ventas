namespace LocalInfantil.Application.Contracts;

public static class TiposMovimientoCuentaCorriente
{
    public const string CargoVenta = "cargo_venta";
    public const string Abono = "abono";
    public const string AjustePositivo = "ajuste_positivo";
    public const string AjusteNegativo = "ajuste_negativo";
    public const string CreditoDevolucion = "credito_devolucion";
    public const string CancelacionDeuda = "cancelacion_deuda";
}

public sealed record NuevoMovimientoCuentaCorriente(
    string ClienteId, string Tipo, long ImporteMinor, string OperacionId,
    string UsuarioId, string CreadoEn, string? VentaId = null, string? CambioId = null,
    string? AbonoId = null, string? Vencimiento = null, string? Motivo = null);

public sealed record CargoCuentaCorriente(string MovimientoId, string? VentaId, string NumeroVenta,
    long ImporteMinor, long PendienteMinor, string Fecha, string? Vencimiento);

public sealed record AplicacionCuentaCorriente(string CargoId, long ImporteMinor,
    string? ComponenteAbonoId = null, int? ComponenteIndice = null, long? SaldoCargoPosteriorMinor = null);

public sealed record ComponenteAbono(string Id, string Medio, long ImporteMinor, long? RecibidoMinor);

public sealed record MovimientoCuentaCorriente(string Id, string Tipo, long ImporteMinor,
    string Fecha, string? Referencia, string? Vencimiento, string? Motivo, long SaldoPosteriorMinor,
    string? VentaId = null, string? CambioId = null, string? AbonoId = null);

public sealed record ResumenCuentaCorriente(string ClienteId, long SaldoMinor, long? LimiteMinor,
    long DisponibleMinor, IReadOnlyList<CargoCuentaCorriente> Cargos);

public enum FiltroClientesCuentaCorriente
{
    Relevantes,
    ConDeuda,
    FiadoHabilitado,
    Todos,
}

public sealed record ClienteCuentaCorrienteListado(
    string Id, string NombreCompleto, string? Telefono, long SaldoMinor,
    long? LimiteMinor, long DisponibleMinor, bool Activo);

public sealed record VistaPreviaAbono(long SaldoAnteriorMinor, long ImporteMinor,
    long SaldoPosteriorMinor, IReadOnlyList<CargoCuentaCorriente> Cargos,
    IReadOnlyList<AplicacionCuentaCorriente> Aplicaciones);

public sealed record DatosAbono(string ClienteId, long ImporteMinor,
    IReadOnlyList<NuevoPago> Pagos, IReadOnlyList<AplicacionCuentaCorriente> AplicacionesEsperadas,
    long SaldoEsperadoMinor, string OperacionId, string? UsuarioSesionId);

public sealed record AbonoRegistrado(string Id, string Numero, string ClienteId,
    long ImporteMinor, string CreadoEn, long SaldoAnteriorMinor, long SaldoPosteriorMinor,
    IReadOnlyList<AplicacionAbonoRecibo> Aplicaciones, IReadOnlyList<PagoTicketAbono> Pagos);

public sealed record AplicacionAbonoRecibo(string VentaNumero, long ImporteMinor, long PendientePosteriorMinor);
public sealed record PagoTicketAbono(string Medio, long ImporteMinor, long? RecibidoMinor, long? VueltoMinor);
public sealed record DocumentoTicketAbono(string Numero, string ClienteNombre, string Fecha,
    long ImporteMinor, long SaldoAnteriorMinor, long SaldoPosteriorMinor,
    IReadOnlyList<PagoTicketAbono> Pagos, IReadOnlyList<AplicacionAbonoRecibo> Aplicaciones)
{
    public string Leyenda => "COMPROBANTE INTERNO — NO FISCAL";
}

public sealed record VistaPreviaAjuste(long SaldoAnteriorMinor, long ImporteFirmadoMinor, long SaldoPosteriorMinor);

public sealed record DatosAjusteCuenta(string ClienteId, bool AumentarDeuda, long ImporteMinor,
    string Motivo, long SaldoEsperadoMinor, string OperacionId, string? UsuarioSesionId);
