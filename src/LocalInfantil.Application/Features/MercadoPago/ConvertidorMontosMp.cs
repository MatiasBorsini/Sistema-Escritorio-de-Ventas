using System.Globalization;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Conversión minor (centavos ARS) ↔ string decimal "1234.56" de la API.
/// ARS tiene siempre 2 decimales; formato invariante, sin separadores.
/// </summary>
public static class ConvertidorMontosMp
{
    public static string ATexto(long importeMinor)
    {
        if (importeMinor <= 0) throw new ArgumentOutOfRangeException(nameof(importeMinor));
        return (importeMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    public static bool IntentaParsear(string? texto, out long importeMinor)
    {
        importeMinor = 0;
        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor))
            return false;
        importeMinor = (long)Math.Round(valor * 100m, MidpointRounding.AwayFromZero);
        return importeMinor > 0;
    }
}
