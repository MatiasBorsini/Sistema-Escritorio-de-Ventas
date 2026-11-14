namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Clasificación de una fila del reporte oficial según RECORD_TYPE + DESCRIPTION.
/// No toda fila es una venta: los saldos, retiros y comisiones no lo son.
/// </summary>
public static class ClasificacionMovimientoMp
{
    public const string Cobro = "cobro";
    public const string Devolucion = "devolucion";
    public const string Contracargo = "contracargo";
    public const string Impuesto = "impuesto";
    public const string PagoFueraSistema = "pago_fuera_sistema";
    public const string Transferencia = "transferencia";
    public const string Saldo = "saldo";
    public const string Otro = "otro";
    public const string Desconocido = "desconocido";

    /// <summary>Clasificaciones que pueden representar una venta/cobro del local.</summary>
    public static bool EsCobrable(string clasificacion) =>
        clasificacion is Cobro or Devolucion or Contracargo;
}

/// <summary>Estados del encabezado de importación (mercado_pago_importacion.estado).</summary>
public static class EstadosImportacionMp
{
    public const string Validado = "validado";
    public const string Aplicado = "aplicado";
    public const string Rechazado = "rechazado";
    public const string Parcial = "parcial";
}

/// <summary>Grado de certeza del vínculo de conciliación.</summary>
public static class TiposMatchConciliacionMp
{
    public const string ExactoExternalRef = "exacto_external_ref";
    public const string ExactoPaymentId = "exacto_payment_id";
    public const string ExactoOrder = "exacto_order";
    public const string AsistidoHumano = "asistido_humano";
}

/// <summary>
/// Identidad financiera remota de una fila, cuando el proveedor aporta un
/// identificador confiable. Se usa SOLO para deduplicar el mismo movimiento
/// entre reportes solapados y para conciliación automática. Nunca se deduce
/// de importe, fecha o descripción.
///
/// La identidad de un MOVIMIENTO financiero es su identificador propio del
/// proveedor (SOURCE_ID = id de transacción/pago/reembolso). NO se usan
/// EXTERNAL_REFERENCE (referencia de la venta) ni ORDER_ID (orden comercial):
/// pueden repetirse en varios movimientos de una misma venta u orden (pagos
/// parciales, comisión, costo financiero, retenciones, reembolsos parciales,
/// contracargos) y colapsarían movimientos distintos, borrando dinero
/// legítimo. Sin un id de movimiento propio, la fila no aporta identidad y
/// queda ambigua (se conserva y se revisa, nunca se descarta).
/// </summary>
public static class IdentidadRemotaMp
{
    /// <summary>Clave confiable o null si la fila no aporta identidad suficiente.</summary>
    public static string? Calcular(FilaLiquidacionMp fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        var movimiento = fila.SourceId;
        if (string.IsNullOrWhiteSpace(movimiento)) return null;
        var prefijo = fila.Clasificacion switch
        {
            ClasificacionMovimientoMp.Cobro => "C:",
            ClasificacionMovimientoMp.Devolucion or ClasificacionMovimientoMp.Contracargo => "R:",
            _ => null
        };
        return prefijo is null ? null : prefijo + movimiento.Trim();
    }
}

/// <summary>Error de una fila del reporte (no aborta el archivo completo).</summary>
public sealed record ErrorFilaLiquidacionMp(int NumeroFila, string Motivo);

/// <summary>
/// Fila del reporte ya parseada y normalizada (centavos enteros, fechas UTC).
/// Es la salida del lector CSV, independiente de la persistencia.
/// </summary>
public sealed record FilaLiquidacionMp
{
    /// <summary>Número de línea física en el archivo (encabezado = 1). Para errores trazables.</summary>
    public int NumeroFila { get; init; }

    /// <summary>Posición de la fila entre las filas de datos, base 1. Es el ordinal de identidad.</summary>
    public int Ordinal { get; init; }
    public string? SourceId { get; init; }
    public string RecordType { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string? FechaTransaccion { get; init; }
    public string? Fecha { get; init; }
    public string? Moneda { get; init; }
    public string? ExternalReference { get; init; }
    public string? OrderMp { get; init; }
    public string? OrderId { get; init; }
    public long? GrossMinor { get; init; }
    public long? NetCreditoMinor { get; init; }
    public long? NetDebitoMinor { get; init; }
    public long? MpFeeMinor { get; init; }
    public long? FinancingFeeMinor { get; init; }
    public long? ShippingFeeMinor { get; init; }
    public long? TaxesMinor { get; init; }
    public long? CouponMinor { get; init; }
    public long? EffectiveCouponMinor { get; init; }
    public long? Installments { get; init; }
    public string? PaymentMethod { get; init; }
    public string? PaymentMethodType { get; init; }
    public string? PosId { get; init; }
    public string? PoiId { get; init; }
    public string? StoreId { get; init; }
    public string? OperationTags { get; init; }
    public string? MetadataJson { get; init; }
    public string Clasificacion { get; init; } = ClasificacionMovimientoMp.Desconocido;

    /// <summary>
    /// Huella SHA-256 del contenido de la fila. NO es única: identifica
    /// contenido repetido para marcar posibles duplicados entre reportes,
    /// nunca para descartar filas. La identidad de la fila es
    /// (importación, ordinal).
    /// </summary>
    public string FilaHash { get; init; } = string.Empty;

    /// <summary>
    /// Identidad financiera remota confiable (ver <see cref="IdentidadRemotaMp"/>).
    /// Null cuando la fila no aporta identificadores suficientes.
    /// </summary>
    public string? IdentidadRemota { get; init; }

    /// <summary>Neto liquidado declarado (crédito − débito). Puede ser negativo.</summary>
    public long NetoMinor => (NetCreditoMinor ?? 0) - (NetDebitoMinor ?? 0);
}

/// <summary>Resultado puro del parseo de un reporte (sin tocar la base).</summary>
public sealed record ParseoReporteMp(
    string FormatoReporte,
    IReadOnlyList<string> Columnas,
    IReadOnlyList<FilaLiquidacionMp> Filas,
    IReadOnlyList<ErrorFilaLiquidacionMp> Errores)
{
    public int FilasReconocidas => Filas.Count(f => f.Clasificacion != ClasificacionMovimientoMp.Desconocido);
    public int FilasDesconocidas => Filas.Count(f => f.Clasificacion == ClasificacionMovimientoMp.Desconocido);
}

/// <summary>Previsualización antes de aplicar (dry-run). No muta nada.</summary>
public sealed record PrevisualizacionImportacionMp(
    string NombreArchivo,
    string HashArchivo,
    string FormatoReporte,
    IReadOnlyList<string> Columnas,
    int FilasTotales,
    int FilasValidas,
    int FilasReconocidas,
    int FilasDesconocidas,
    int FilasDuplicadas,
    int FilasConError,
    bool YaImportado,
    IReadOnlyList<ErrorFilaLiquidacionMp> Errores);

/// <summary>Resultado de aplicar una importación.</summary>
public sealed record ResultadoImportacionMp(
    string ImportacionId,
    string Estado,
    int FilasTotales,
    int FilasAplicadas,
    int FilasDuplicadas,
    int FilasError,
    int ConciliadasAuto,
    int Pendientes,
    int FilasEnRevision);

/// <summary>Importación persistida (lectura).</summary>
public sealed record ImportacionMpRegistrada
{
    public string Id { get; init; } = string.Empty;
    public string Origen { get; init; } = string.Empty;
    public string? ArchivoNombre { get; init; }
    public string? ArchivoHash { get; init; }
    public string? CuentaMp { get; init; }
    public string? FormatoReporte { get; init; }
    public string? ZonaHoraria { get; init; }
    public string? RangoDesde { get; init; }
    public string? RangoHasta { get; init; }
    public long FilasTotales { get; init; }
    public long FilasAplicadas { get; init; }
    public long FilasError { get; init; }
    public string Estado { get; init; } = string.Empty;
    public string? UsuarioId { get; init; }
    public string CreadoEn { get; init; } = string.Empty;
}

/// <summary>Fila de liquidación persistida (lectura).</summary>
public sealed record LiquidacionMpRegistrada
{
    public string Id { get; init; } = string.Empty;
    public string ImportacionId { get; init; } = string.Empty;
    public long FilaOrdinal { get; init; }
    public string FilaHash { get; init; } = string.Empty;
    public string? IdentidadRemota { get; init; }
    public string Fuente { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string RecordType { get; init; } = string.Empty;
    public string Descripcion { get; init; } = string.Empty;
    public string Clasificacion { get; init; } = string.Empty;
    public string? FechaTransaccion { get; init; }
    public string? Fecha { get; init; }
    public string? Moneda { get; init; }
    public string? ExternalReference { get; init; }
    public string? OrderMp { get; init; }
    public string? OrderId { get; init; }
    public long? GrossMinor { get; init; }
    public long? NetCreditoMinor { get; init; }
    public long? NetDebitoMinor { get; init; }
    public long? MpFeeMinor { get; init; }
    public long? FinancingFeeMinor { get; init; }
    public long? ShippingFeeMinor { get; init; }
    public long? TaxesMinor { get; init; }
    public long? CouponMinor { get; init; }
    public long? EffectiveCouponMinor { get; init; }
    public long? Installments { get; init; }
    public string? PaymentMethod { get; init; }
    public string? PaymentMethodType { get; init; }
    public string? PosId { get; init; }
    public string? PoiId { get; init; }
    public string? StoreId { get; init; }
    public string? OperationTags { get; init; }
    public string? MetadataJson { get; init; }
    public string EstadoConciliacion { get; init; } = string.Empty;
    public string? VentaId { get; init; }
    public string? PagoId { get; init; }
    public string? OperacionMpId { get; init; }
    public string? CobroManualId { get; init; }
    public string? Observacion { get; init; }
    public string CreadoEn { get; init; } = string.Empty;

    /// <summary>Neto liquidado declarado (crédito − débito). Puede ser negativo.</summary>
    public long NetoMinor => (NetCreditoMinor ?? 0) - (NetDebitoMinor ?? 0);
}

/// <summary>Candidato (nunca automático) para conciliación asistida.</summary>
public sealed record CandidatoConciliacionMp(
    string Origen,
    string? VentaId,
    string? VentaNumero,
    string? PagoId,
    string? CobroManualId,
    string? OperacionMpId,
    long ImporteMinor,
    string Fecha,
    string Detalle);

/// <summary>Resultado de una conciliación (automática o asistida).</summary>
public sealed record ResultadoConciliacionLiquidacionMp(
    string LiquidacionId,
    string EstadoConciliacion,
    string? TipoMatch,
    string? VentaId,
    string? PagoId,
    string? OperacionMpId,
    string? CobroManualId,
    string? DiferenciaDetalle);
