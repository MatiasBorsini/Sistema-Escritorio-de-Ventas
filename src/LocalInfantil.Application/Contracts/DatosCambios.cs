namespace LocalInfantil.Application.Contracts;

public sealed record LineaCambioEntrada(string LineaVentaId, long Cantidad, bool Apta, string? MotivoNoApta);
public sealed record LineaCambioSalida(string VarianteId, long Cantidad, long PrecioEsperadoMinor);
public sealed record PagoDiferenciaCambio(string Medio, long ImporteMinor, long? RecibidoMinor);
public enum DecisionCuentaCorrienteCambio { AplicarADeuda, Reintegrar }
public sealed record DatosCambio(string VentaId, IReadOnlyList<LineaCambioEntrada> Entradas, IReadOnlyList<LineaCambioSalida> Salidas, IReadOnlyList<PagoDiferenciaCambio> Cobros, string? MedioReintegro, string? Motivo, string OperacionId, string? UsuarioSesionId, string? AutorizanteNombre = null, string? AutorizanteContrasena = null, DecisionCuentaCorrienteCambio? DecisionCuenta = null, bool ConfirmarDecisionCuenta = false, long? DeudaCuentaEsperadaMinor = null);
public sealed record DetallePrecioCambioCambiado(string VarianteId, string NombreProducto, long PrecioEsperadoMinor, long PrecioVigenteMinor);
public abstract record ResultadoCambio
{
    public sealed record Registrado(string CambioId, string Numero, long ImporteEntradasMinor, long ImporteSalidasMinor, long DiferenciaMinor, string Fecha) : ResultadoCambio;
    public sealed record PrecioCambiado(IReadOnlyList<DetallePrecioCambioCambiado> Cambios) : ResultadoCambio;
    public sealed record DecisionCuentaRequerida(long DeudaVentaMinor, long CreditoDevolucionMinor) : ResultadoCambio;
    public sealed record ConfirmacionCuenta(DecisionCuentaCorrienteCambio Decision, long DeudaAnteriorMinor, long CreditoMinor, long AplicarMinor, long DeudaPosteriorMinor, long ReintegrarMinor) : ResultadoCambio;
}
public sealed record ResumenCambio(string Id, string Numero, string VentaNumero, long DiferenciaMinor, string Fecha, string? Responsable, bool TieneArticulosNuevos = true, long ImporteAplicadoDeudaMinor = 0)
{
    public string TipoTexto => DiferenciaMinor == 0 ? "Sin diferencia" : TieneArticulosNuevos ? "Cambio" : "Devolución";
    public string ResultadoTexto => DiferenciaMinor > 0 ? "Cobrado" : DiferenciaMinor == 0 ? "Sin diferencia"
        : ImporteAplicadoDeudaMinor == -DiferenciaMinor ? "Aplicado a deuda"
        : ImporteAplicadoDeudaMinor > 0 ? "Reintegro y deuda" : "Reintegrado";
    public long ImporteResultadoMinor => Math.Abs(DiferenciaMinor);
}
public sealed record LineaCambioDisponible(LineaVentaRegistrada Linea, long CantidadDisponible);
public sealed record OpcionVarianteCambio(string VarianteId, string CodigoInterno, string Nombre, string? Talle, string? Color, long PrecioMinor, long Disponible);
public sealed record LineaTicketCambio(string Direccion, string CodigoInterno, string NombreProducto, string? Talle, string? Color, long Cantidad, long PrecioUnitarioMinor, long SubtotalMinor, bool Apta, string? MotivoNoApta)
{
    public string DireccionTexto => Direccion == "entrada" ? "Prenda que vuelve" : "Artículo nuevo";
    public string AptitudTexto => Direccion != "entrada" ? string.Empty : Apta ? "Apta para volver al inventario disponible" : "No apta · queda apartada";
}
public sealed record PagoTicketCambio(string Direccion, string Medio, long ImporteMinor, long? RecibidoMinor)
{
    public string DireccionTexto => Direccion == "reintegro" ? "Importe reintegrado" : "Importe cobrado";
    public string MedioTexto => Medio switch
    {
        "efectivo" => "Efectivo", "transferencia" => "Transferencia", "debito" => "Débito",
        "credito" => "Crédito", "billetera" => "Billetera", "mercado_pago" => "Mercado Pago", _ => Medio
    };
    public string? EtiquetaRecibidoTexto => Direccion == "reintegro" ? "Efectivo entregado" : "Efectivo recibido";
    public long? VueltoMinor => Direccion == "cobro" && RecibidoMinor.HasValue ? RecibidoMinor.Value - ImporteMinor : null;
}
public sealed record DocumentoTicketCambio(string Numero, string VentaNumero, string Fecha, string Responsable, string? Autorizante, string? Motivo, long ImporteEntradasMinor, long ImporteSalidasMinor, long DiferenciaMinor, IReadOnlyList<LineaTicketCambio> Lineas, IReadOnlyList<PagoTicketCambio> Pagos, string? DecisionCuenta = null, long ImporteAplicadoDeudaMinor = 0, long? DeudaAnteriorMinor = null, long? DeudaPosteriorMinor = null)
{
    public string Leyenda => "COMPROBANTE INTERNO — NO FISCAL";
    public string DecisionCuentaTexto => DecisionCuenta switch { "aplicar_a_deuda" => "Crédito aplicado a la deuda de la venta original", "reintegrar" => "Reintegro elegido; la deuda se mantiene", _ => string.Empty };
}
