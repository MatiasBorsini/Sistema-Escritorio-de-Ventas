using LocalInfantil.Application.Features.Reportes;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// Día comercial = día local: [00:00 inclusive, 00:00 exclusiva del día
/// siguiente]. Nunca 23:59:59 como límite.
/// </summary>
public sealed class RangoFechasTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Dia_simple_en_utc()
    {
        var rango = RangoFechas.Dia(new DateOnly(2026, 10, 5), Utc);
        Assert.Equal("2026-10-05T00:00:00.0000000+00:00", rango.DesdeUtc);
        Assert.Equal("2026-10-06T00:00:00.0000000+00:00", rango.HastaUtc);
    }

    [Fact]
    public void Rango_excluye_fin_y_cubre_milisegundos()
    {
        var rango = RangoFechas.PorDias(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), Utc);
        Assert.Equal("2026-10-05T00:00:00.0000000+00:00", rango.DesdeUtc);
        Assert.Equal("2026-10-07T00:00:00.0000000+00:00", rango.HastaUtc);
        // El último milisegundo del día 6 entra; la medianoche del 7 no.
        Assert.True(string.Compare("2026-10-06T23:59:59.9999999+00:00", rango.HastaUtc, StringComparison.Ordinal) < 0);
        Assert.True(string.Compare("2026-10-06T23:59:59.9999999+00:00", rango.DesdeUtc, StringComparison.Ordinal) >= 0);
        Assert.True(string.Compare("2026-10-07T00:00:00.0000000+00:00", rango.HastaUtc, StringComparison.Ordinal) == 0);
    }

    [Fact]
    public void Zona_menos_tres_desplaza_limites()
    {
        var zona = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
        var rango = RangoFechas.Dia(new DateOnly(2026, 10, 5), zona);
        Assert.Equal("2026-10-05T03:00:00.0000000+00:00", rango.DesdeUtc);
        Assert.Equal("2026-10-06T03:00:00.0000000+00:00", rango.HastaUtc);
    }

    [Fact]
    public void Medianoche_local_pertenece_al_dia_que_empieza()
    {
        // 2026-10-05 00:00 local (UTC-3) = 03:00Z: dentro del día 5, no del 4.
        var zona = TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");
        var rango = RangoFechas.Dia(new DateOnly(2026, 10, 5), zona);
        Assert.True(string.Compare("2026-10-05T03:00:00.0000000+00:00", rango.DesdeUtc, StringComparison.Ordinal) >= 0);
        Assert.True(string.Compare("2026-10-05T03:00:00.0000000+00:00", rango.HastaUtc, StringComparison.Ordinal) < 0);
        var anterior = RangoFechas.Dia(new DateOnly(2026, 10, 4), zona);
        Assert.True(string.Compare("2026-10-05T03:00:00.0000000+00:00", anterior.HastaUtc, StringComparison.Ordinal) >= 0);
    }

    [Fact]
    public void Fin_anterior_al_inicio_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => RangoFechas.PorDias(new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 5), Utc));
    }
}
