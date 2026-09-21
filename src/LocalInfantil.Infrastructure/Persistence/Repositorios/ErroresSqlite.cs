using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

/// <summary>
/// Traduce errores SQLite a excepciones comprensibles para Application.
/// Solo los bloqueos (BUSY/LOCKED) se traducen: SQLITE_INTERRUPT (9) no es
/// un bloqueo y se propaga tal cual para no disfrazar el diagnóstico.
/// </summary>
internal static class ErroresSqlite
{
    private const int Ocupado = 5; // SQLITE_BUSY
    private const int Bloqueado = 6; // SQLITE_LOCKED

    public static bool EsBloqueo(SqliteException excepcion)
    {
        return excepcion.SqliteErrorCode == Ocupado
            || excepcion.SqliteErrorCode == Bloqueado;
    }

    public static AlmacenamientoOcupadoException OcupadoException(SqliteException excepcion)
    {
        return new AlmacenamientoOcupadoException(
            "La base de datos está ocupada por otra operación. Reintentá la operación.",
            excepcion);
    }
}
