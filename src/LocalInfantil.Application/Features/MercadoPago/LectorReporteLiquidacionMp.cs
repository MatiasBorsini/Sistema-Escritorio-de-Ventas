using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Lector puro del reporte oficial de dinero liberado (CSV) de Mercado Pago.
/// No toca la base: transforma texto en filas normalizadas y errores por fila.
/// El mapa de columnas se arma por nombre técnico (key), tolerando traducción,
/// mayúsculas y variaciones de separadores de palabra.
/// </summary>
public static class LectorReporteLiquidacionMp
{
    private static readonly string[] ColumnasRequeridas =
    {
        "RECORD_TYPE", "DESCRIPTION", "DATE"
    };

    private static readonly Dictionary<string, string> AliasColumnas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FECHA"] = "DATE",
        ["FECHA_DE_LIQUIDACION"] = "DATE",
        ["FECHA_DE_ACREDITACION"] = "DATE",
        ["TRANSACTION_DATE"] = "TRANSACTION_DATE",
        ["FECHA_DE_TRANSACCION"] = "TRANSACTION_DATE",
        ["TIPO_DE_REGISTRO"] = "RECORD_TYPE",
        ["TIPO_REGISTRO"] = "RECORD_TYPE",
        ["DESCRIPCION"] = "DESCRIPTION",
        ["ID_DE_OPERACION"] = "SOURCE_ID",
        ["ID_OPERACION_MP"] = "SOURCE_ID",
        ["ID_DE_PAGO"] = "SOURCE_ID",
        ["PAYMENT_ID"] = "SOURCE_ID",
        ["REFERENCIA_EXTERNA"] = "EXTERNAL_REFERENCE",
        ["MONTO_BRUTO"] = "GROSS_AMOUNT",
        ["IMPORTE_BRUTO"] = "GROSS_AMOUNT",
        ["COMISION_MP"] = "MP_FEE_AMOUNT",
        ["COMISION_MERCADOPAGO"] = "MP_FEE_AMOUNT",
        ["COSTO_DE_FINANCIACION"] = "FINANCING_FEE_AMOUNT",
        ["IMPUESTOS"] = "TAXES_AMOUNT",
        ["MONTO_NETO"] = "NET_CREDIT_AMOUNT",
        ["IMPORTE_CREDITO_NETO"] = "NET_CREDIT_AMOUNT",
        ["IMPORTE_DEBITO_NETO"] = "NET_DEBIT_AMOUNT",
        ["TOTAL_A_COBRAR"] = "NET_CREDIT_AMOUNT",
        ["CANTIDAD_DE_CUOTAS"] = "INSTALLMENTS",
        ["CUOTAS"] = "INSTALLMENTS",
        ["MEDIO_DE_PAGO"] = "PAYMENT_METHOD",
        ["TIPO_DE_MEDIO_DE_PAGO"] = "PAYMENT_METHOD_TYPE",
        ["MONEDA"] = "CURRENCY",
        ["ETIQUETAS_DE_OPERACION"] = "OPERATION_TAGS"
    };

    /// <summary>
    /// Decodifica el contenido respetando BOM (UTF-8/UTF-16 LE/BE). Sin BOM asume UTF-8,
    /// que es el formato de los reportes oficiales.
    /// </summary>
    public static string Decodificar(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Hash SHA-256 (hex) del archivo físico, usado como clave de importación.</summary>
    public static string HashArchivo(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static ParseoReporteMp Parsear(string contenido, string nombreArchivo)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        var registros = Tokenizar(contenido, out var delimitador);
        if (registros.Count == 0)
            throw new FormatoReporteInvalidoMpException("El archivo no contiene filas.");

        var encabezados = registros[0].Select(NormalizarEncabezado).ToArray();
        var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < encabezados.Length; i++)
        {
            var canonica = Canonica(encabezados[i]);
            if (!string.IsNullOrEmpty(canonica) && !indice.ContainsKey(canonica))
                indice[canonica] = i;
        }

        foreach (var requerida in ColumnasRequeridas)
        {
            if (!indice.ContainsKey(requerida))
                throw new FormatoReporteInvalidoMpException(
                    $"Al reporte '{nombreArchivo}' le falta la columna obligatoria '{requerida}'. No se puede interpretar como conciliación.");
        }

        var filas = new List<FilaLiquidacionMp>();
        var errores = new List<ErrorFilaLiquidacionMp>();
        for (var r = 1; r < registros.Count; r++)
        {
            var registro = registros[r];
            if (registro.All(string.IsNullOrWhiteSpace))
                continue;

            try
            {
                filas.Add(ConstruirFila(r + 1, registro, indice, delimitador));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                errores.Add(new ErrorFilaLiquidacionMp(r + 1, ex.Message));
            }
        }

        var columnas = encabezados.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray();
        var formato = $"csv:{delimitador}:{string.Join("|", indice.Keys.OrderBy(k => k, StringComparer.Ordinal))}";
        return new ParseoReporteMp(formato, columnas, filas, errores);
    }

    private static FilaLiquidacionMp ConstruirFila(
        int numero,
        IReadOnlyList<string> registro,
        IReadOnlyDictionary<string, int> indice,
        char delimitador)
    {
        string? Valor(string columna) =>
            indice.TryGetValue(columna, out var i) && i < registro.Count
                ? NullIfEmpty(registro[i])
                : null;

        var recordType = Valor("RECORD_TYPE") ?? string.Empty;
        var descripcion = Valor("DESCRIPTION") ?? string.Empty;
        var fecha = NormalizarFecha(Valor("DATE"), "DATE");
        var fechaTransaccion = NormalizarFecha(Valor("TRANSACTION_DATE"), "TRANSACTION_DATE");

        string? fuente = Valor("SOURCE_ID");
        var externalReference = Valor("EXTERNAL_REFERENCE");
        var orderMp = Valor("ORDER_MP") ?? Valor("ORDER_ID");
        var orderId = Valor("ORDER_ID");

        var gross = ParsearMonto(Valor("GROSS_AMOUNT"), "GROSS_AMOUNT");
        var netCredito = ParsearMonto(Valor("NET_CREDIT_AMOUNT"), "NET_CREDIT_AMOUNT");
        var netDebito = ParsearMonto(Valor("NET_DEBIT_AMOUNT"), "NET_DEBIT_AMOUNT");
        var mpFee = ParsearMonto(Valor("MP_FEE_AMOUNT"), "MP_FEE_AMOUNT");
        var financingFee = ParsearMonto(Valor("FINANCING_FEE_AMOUNT"), "FINANCING_FEE_AMOUNT");
        var shippingFee = ParsearMonto(Valor("SHIPPING_FEE_AMOUNT"), "SHIPPING_FEE_AMOUNT");
        var taxes = ParsearMonto(Valor("TAXES_AMOUNT"), "TAXES_AMOUNT");
        var coupon = ParsearMonto(Valor("COUPON_AMOUNT"), "COUPON_AMOUNT");
        var effectiveCoupon = ParsearMonto(Valor("EFFECTIVE_COUPON_AMOUNT"), "EFFECTIVE_COUPON_AMOUNT");
        var installments = ParsearEntero(Valor("INSTALLMENTS"), "INSTALLMENTS");

        var clasificacion = Clasificar(recordType, descripcion, netCredito, netDebito);

        var fila = new FilaLiquidacionMp
        {
            NumeroFila = numero,
            Ordinal = numero - 1,
            SourceId = fuente,
            RecordType = recordType,
            Descripcion = descripcion,
            Fecha = fecha,
            FechaTransaccion = fechaTransaccion,
            Moneda = Valor("CURRENCY"),
            ExternalReference = externalReference,
            OrderMp = orderMp,
            OrderId = orderId,
            GrossMinor = gross,
            NetCreditoMinor = netCredito,
            NetDebitoMinor = netDebito,
            MpFeeMinor = mpFee,
            FinancingFeeMinor = financingFee,
            ShippingFeeMinor = shippingFee,
            TaxesMinor = taxes,
            CouponMinor = coupon,
            EffectiveCouponMinor = effectiveCoupon,
            Installments = installments,
            PaymentMethod = Valor("PAYMENT_METHOD"),
            PaymentMethodType = Valor("PAYMENT_METHOD_TYPE"),
            PosId = Valor("POS_ID") ?? Valor("POI_ID"),
            PoiId = Valor("POI_ID"),
            StoreId = Valor("STORE_ID"),
            OperationTags = Valor("OPERATION_TAGS"),
            MetadataJson = Valor("METADATA"),
            Clasificacion = clasificacion
        };
        return fila with { FilaHash = CalcularFilaHash(fila, delimitador), IdentidadRemota = IdentidadRemotaMp.Calcular(fila) };
    }

    private static string Clasificar(string recordType, string descripcion, long? credito, long? debito)
    {
        var tipo = recordType.Trim().ToLowerInvariant();
        var desc = descripcion.Trim().ToLowerInvariant();

        if (tipo.Contains("chargeback") || desc.Contains("chargeback") || desc.Contains("contracargo"))
            return ClasificacionMovimientoMp.Contracargo;
        if (tipo.Contains("refund") || desc.Contains("refund") || desc.Contains("devol") || desc.Contains("revers"))
            return ClasificacionMovimientoMp.Devolucion;

        if (tipo.StartsWith("initial_available_balance") || tipo == "available_balance" ||
            tipo == "balance" || tipo == "total" || tipo.Contains("balance"))
            return ClasificacionMovimientoMp.Saldo;

        if (tipo.StartsWith("payout") || tipo.Contains("payout") || desc.Contains("transferencia") || desc.Contains("retiro"))
            return ClasificacionMovimientoMp.Transferencia;

        if (tipo.Contains("tax") || desc.Contains("tax") || desc.Contains("impuesto") ||
            desc.Contains("iva") || desc.Contains("iibb") || desc.Contains("retenc") || desc.Contains("percep"))
            return ClasificacionMovimientoMp.Impuesto;

        if (tipo is "release" or "payment" or "payment_release" or "release_payment" ||
            desc.StartsWith("payment") || desc.StartsWith("pago"))
            return ClasificacionMovimientoMp.Cobro;

        if (tipo.Length == 0 && desc.Length == 0)
            return ClasificacionMovimientoMp.Desconocido;

        return ClasificacionMovimientoMp.Desconocido;
    }

    private static string CalcularFilaHash(FilaLiquidacionMp fila, char delimitador)
    {
        var partes = new[]
        {
            fila.SourceId, fila.RecordType, fila.Descripcion, fila.Fecha, fila.FechaTransaccion,
            fila.ExternalReference, fila.OrderMp, fila.OrderId, fila.Moneda,
            fila.GrossMinor?.ToString(CultureInfo.InvariantCulture),
            fila.NetCreditoMinor?.ToString(CultureInfo.InvariantCulture),
            fila.NetDebitoMinor?.ToString(CultureInfo.InvariantCulture),
            fila.MpFeeMinor?.ToString(CultureInfo.InvariantCulture),
            fila.FinancingFeeMinor?.ToString(CultureInfo.InvariantCulture),
            fila.TaxesMinor?.ToString(CultureInfo.InvariantCulture),
            fila.Installments?.ToString(CultureInfo.InvariantCulture),
            fila.PaymentMethod, fila.PaymentMethodType, fila.PoiId
        };
        var normalizado = string.Join('\u001f', partes.Select(p => p ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizado))).ToLowerInvariant();
    }

    private static long? ParsearMonto(string? valor, string columna)
    {
        if (valor is null) return null;
        var texto = valor.Trim();
        if (texto.Length == 0) return null;

        var negativo = false;
        if (texto[0] is '-' or '+')
        {
            negativo = texto[0] == '-';
            texto = texto[1..].Trim();
        }

        var punto = texto.IndexOf('.');
        string enteros;
        string? decimales;
        if (punto < 0)
        {
            enteros = texto;
            decimales = null;
        }
        else
        {
            enteros = texto[..punto];
            decimales = texto[(punto + 1)..];
        }

        if (enteros.Length == 0) enteros = "0";
        if (!enteros.All(char.IsDigit) || (decimales is not null && !decimales.All(char.IsDigit)))
            throw new FormatException($"Monto inválido en '{columna}': '{valor}'.");
        if (decimales is { Length: > 2 })
            throw new FormatException($"'{columna}' admite hasta 2 decimales: '{valor}'.");

        decimales = (decimales ?? string.Empty).PadRight(2, '0');
        var entero = long.Parse(enteros, CultureInfo.InvariantCulture);
        var centavos = decimales.Length == 0 ? 0 : int.Parse(decimales, CultureInfo.InvariantCulture);
        var minor = checked(entero * 100 + centavos);
        return negativo ? -minor : minor;
    }

    private static long? ParsearEntero(string? valor, string columna)
    {
        if (valor is null) return null;
        var texto = valor.Trim();
        if (texto.Length == 0) return null;
        if (!texto.All(char.IsDigit))
            throw new FormatException($"Entero inválido en '{columna}': '{valor}'.");
        return long.Parse(texto, CultureInfo.InvariantCulture);
    }

    private static string? NormalizarFecha(string? valor, string columna)
    {
        if (valor is null) return null;
        var texto = valor.Trim();
        if (texto.Length == 0) return null;
        if (!DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
            throw new FormatException($"Fecha inválida en '{columna}': '{valor}'.");
        return dto.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
    }

    private static string NormalizarEncabezado(string encabezado)
    {
        var sb = new StringBuilder(encabezado.Length);
        var previoSeparador = false;
        foreach (var c in encabezado.Trim())
        {
            if (c is ' ' or '\t' or '-' or '.' or '_')
            {
                if (!previoSeparador && sb.Length > 0) sb.Append('_');
                previoSeparador = true;
                continue;
            }

            var plano = QuitarAcento(char.ToUpperInvariant(c));
            sb.Append(plano);
            previoSeparador = false;
        }

        return sb.ToString().Trim('_');
    }

    private static char QuitarAcento(char c) => c switch
    {
        'Á' or 'À' or 'Ä' or 'Â' => 'A',
        'É' or 'È' or 'Ë' or 'Ê' => 'E',
        'Í' or 'Ì' or 'Ï' or 'Î' => 'I',
        'Ó' or 'Ò' or 'Ö' or 'Ô' => 'O',
        'Ú' or 'Ù' or 'Ü' or 'Û' => 'U',
        'Ñ' => 'N',
        _ => c
    };

    private static string? Canonica(string encabezadoNormalizado)
    {
        if (string.IsNullOrEmpty(encabezadoNormalizado)) return null;
        return AliasColumnas.TryGetValue(encabezadoNormalizado, out var canonica)
            ? canonica
            : encabezadoNormalizado;
    }

    private static string? NullIfEmpty(string? valor)
    {
        if (valor is null) return null;
        var texto = valor.Trim();
        return texto.Length == 0 ? null : texto;
    }

    /// <summary>Tokenizador CSV RFC 4180 con detección de delimitador (, o ;).</summary>
    private static List<List<string>> Tokenizar(string contenido, out char delimitador)
    {
        delimitador = DetectarDelimitador(contenido);
        var registros = new List<List<string>>();
        var campo = new StringBuilder();
        var registro = new List<string>();
        var entreComillas = false;

        for (var i = 0; i < contenido.Length; i++)
        {
            var c = contenido[i];
            if (entreComillas)
            {
                if (c == '"')
                {
                    if (i + 1 < contenido.Length && contenido[i + 1] == '"')
                    {
                        campo.Append('"');
                        i++;
                    }
                    else
                    {
                        entreComillas = false;
                    }
                }
                else
                {
                    campo.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                entreComillas = true;
            }
            else if (c == delimitador)
            {
                registro.Add(campo.ToString());
                campo.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < contenido.Length && contenido[i + 1] == '\n') i++;
                registro.Add(campo.ToString());
                campo.Clear();
                registros.Add(registro);
                registro = new List<string>();
            }
            else
            {
                campo.Append(c);
            }
        }

        if (campo.Length > 0 || registro.Count > 0)
        {
            registro.Add(campo.ToString());
            registros.Add(registro);
        }

        return registros;
    }

    private static char DetectarDelimitador(string contenido)
    {
        var comas = 0;
        var puntosYComa = 0;
        var entreComillas = false;
        foreach (var c in contenido)
        {
            if (c == '"')
            {
                entreComillas = !entreComillas;
            }
            else if (c is '\r' or '\n' && !entreComillas)
            {
                break;
            }
            else if (!entreComillas)
            {
                if (c == ',') comas++;
                else if (c == ';') puntosYComa++;
            }
        }

        return puntosYComa > comas ? ';' : ',';
    }
}

/// <summary>El CSV no tiene el formato mínimo esperado. No debe interpretarse como conciliación.</summary>
public sealed class FormatoReporteInvalidoMpException : Exception
{
    public FormatoReporteInvalidoMpException(string mensaje) : base(mensaje)
    {
    }
}
