namespace LocalInfantil.Application.Contracts;

public sealed record LineaCambioEntrada(string LineaVentaId, long Cantidad, bool Apta, string? MotivoNoApta);
public sealed record LineaCambioSalida(string VarianteId, long Cantidad, long PrecioEsperadoMinor);
public sealed record PagoDiferenciaCambio(string Medio, long ImporteMinor, long? RecibidoMinor);
public sealed record DatosCambio(string VentaId, IReadOnlyList<LineaCambioEntrada> Entradas, IReadOnlyList<LineaCambioSalida> Salidas, IReadOnlyList<PagoDiferenciaCambio> Cobros, string? MedioReintegro, string? Motivo, string OperacionId, string? UsuarioSesionId, string? AutorizanteNombre = null, string? AutorizanteContrasena = null);
public sealed record DetallePrecioCambioCambiado(string VarianteId, string NombreProducto, long PrecioEsperadoMinor, long PrecioVigenteMinor);
public abstract record ResultadoCambio
{
    public sealed record Registrado(string CambioId, string Numero, long ImporteEntradasMinor, long ImporteSalidasMinor, long DiferenciaMinor, string Fecha) : ResultadoCambio;
    public sealed record PrecioCambiado(IReadOnlyList<DetallePrecioCambioCambiado> Cambios) : ResultadoCambio;
}
public sealed record ResumenCambio(string Id, string Numero, string VentaNumero, long DiferenciaMinor, string Fecha, string? Responsable);
public sealed record LineaCambioDisponible(LineaVentaRegistrada Linea, long CantidadDisponible);
public sealed record OpcionVarianteCambio(string VarianteId, string CodigoInterno, string Nombre, string? Talle, string? Color, long PrecioMinor, long Disponible);
public sealed record LineaTicketCambio(string Direccion, string CodigoInterno, string NombreProducto, string? Talle, string? Color, long Cantidad, long PrecioUnitarioMinor, long SubtotalMinor, bool Apta, string? MotivoNoApta);
public sealed record PagoTicketCambio(string Direccion, string Medio, long ImporteMinor, long? RecibidoMinor)
{
    public long? VueltoMinor => RecibidoMinor.HasValue ? RecibidoMinor.Value - ImporteMinor : null;
}
public sealed record DocumentoTicketCambio(string Numero, string VentaNumero, string Fecha, string Responsable, string? Autorizante, string? Motivo, long ImporteEntradasMinor, long ImporteSalidasMinor, long DiferenciaMinor, IReadOnlyList<LineaTicketCambio> Lineas, IReadOnlyList<PagoTicketCambio> Pagos)
{
    public string Leyenda => "COMPROBANTE INTERNO — NO FISCAL";
}
