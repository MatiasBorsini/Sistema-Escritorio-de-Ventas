using LocalInfantil.Infrastructure.Persistence.Repositorios;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

/// <summary>
/// Verifica que solo los bloqueos reales (5 y 6) se traduzcan a
/// <see cref="LocalInfantil.Application.Abstractions.AlmacenamientoOcupadoException"/>:
/// SQLITE_INTERRUPT (9) y el resto de errores se propagan sin disfrazar,
/// conservando siempre la excepción original como causa.
/// </summary>
public sealed class MapeoErroresSqliteTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void Bloqueos_se_traducen_con_causa_original(int codigo)
    {
        var original = new SqliteException("simulado", codigo);

        Assert.True(ErroresSqlite.EsBloqueo(original));

        var traducida = ErroresSqlite.OcupadoException(original);
        Assert.Same(original, traducida.InnerException);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(1)]
    [InlineData(19)]
    public void Otros_errores_no_son_bloqueo(int codigo)
    {
        var original = new SqliteException("simulado", codigo);

        Assert.False(ErroresSqlite.EsBloqueo(original));
    }
}
