namespace LocalInfantil.Application.Features.Reportes;

/// <summary>
/// Período semiabierto [DesdeUtc, HastaUtc) en ISO-8601. Las fechas
/// persistidas son UTC ("o"); la comparación lexicográfica es válida.
/// </summary>
public sealed record PeriodoReporte(string DesdeUtc, string HastaUtc);

/// <summary>
/// Día comercial = día local de la PC. Filtros inclusivos por día:
/// desde 00:00 inclusive hasta las 00:00 exclusivas del día siguiente.
/// Nunca usar 23:59:59 como límite.
/// </summary>
public static class RangoFechas
{
    public static PeriodoReporte PorDias(DateOnly desde, DateOnly hastaInclusive, TimeZoneInfo? zona = null)
    {
        if (hastaInclusive < desde)
        {
            throw new ArgumentException("El fin del período no puede ser anterior al inicio.", nameof(hastaInclusive));
        }

        var z = zona ?? TimeZoneInfo.Local;
        var inicioLocal = new DateTime(desde.Year, desde.Month, desde.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var finLocal = new DateTime(hastaInclusive.Year, hastaInclusive.Month, hastaInclusive.Day, 0, 0, 0, DateTimeKind.Unspecified).AddDays(1);
        var desdeUtc = new DateTimeOffset(inicioLocal, z.GetUtcOffset(inicioLocal)).ToUniversalTime();
        var hastaUtc = new DateTimeOffset(finLocal, z.GetUtcOffset(finLocal)).ToUniversalTime();
        return new PeriodoReporte(desdeUtc.ToString("o"), hastaUtc.ToString("o"));
    }

    public static PeriodoReporte Dia(DateOnly dia, TimeZoneInfo? zona = null) => PorDias(dia, dia, zona);
}
