using LocalInfantil.Application.Abstractions;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Sesión SQLite con control explícito de la transacción por SQL.
/// Las sesiones de escritura abren con BEGIN IMMEDIATE (toman el bloqueo de
/// escritura al iniciar, sin sorpresas a mitad de la operación); las de
/// lectura no abren transacción y nunca bloquean escritores. Todos los
/// comandos usan el tiempo de espera configurado, de modo que una contienda
/// falla rápido en lugar de colgar. Liberar una sesión de escritura sin
/// confirmar revierte. Uso interno de los repositorios vía <see cref="ISesionSqlite"/>.
/// </summary>
internal interface ISesionSqlite
{
    SqliteConnection Conexion { get; }

    int TimeoutSegundos { get; }

    bool SoloLectura { get; }
}

internal sealed class SesionPersistenciaSqlite : ISesionPersistencia, ISesionSqlite
{
    private readonly SqliteConnection _conexion;
    private readonly int _timeoutSegundos;
    private readonly bool _soloLectura;
    private bool _activa;
    private bool _terminada;

    public SesionPersistenciaSqlite(SqliteConnection conexion, int timeoutSegundos, bool soloLectura, bool transaccionAbierta)
    {
        _conexion = conexion;
        _timeoutSegundos = timeoutSegundos;
        _soloLectura = soloLectura;
        _activa = transaccionAbierta;
    }

    public SqliteConnection Conexion => _conexion;

    public int TimeoutSegundos => _timeoutSegundos;

    public bool SoloLectura => _soloLectura;

    public async Task ConfirmarAsync(CancellationToken cancellationToken = default)
    {
        if (_terminada)
        {
            throw new InvalidOperationException("La sesión ya terminó.");
        }

        if (_soloLectura)
        {
            throw new InvalidOperationException("La sesión es de solo lectura y no admite confirmación.");
        }

        try
        {
            await EjecutarAsync("COMMIT;", cancellationToken);
        }
        catch
        {
            await IntentarRevertirAsync();
            throw;
        }

        _activa = false;
        _terminada = true;
    }

    public async Task RevertirAsync(CancellationToken cancellationToken = default)
    {
        if (!_terminada)
        {
            if (_activa)
            {
                await IntentarRevertirAsync();
                _activa = false;
            }

            _terminada = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_terminada)
        {
            if (_activa)
            {
                await IntentarRevertirAsync();
                _activa = false;
            }

            _terminada = true;
        }

        await _conexion.DisposeAsync();
    }

    private async Task EjecutarAsync(string sql, CancellationToken cancellationToken)
    {
        using var comando = _conexion.CreateCommand();
        comando.CommandText = sql;
        comando.CommandTimeout = _timeoutSegundos;
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task IntentarRevertirAsync()
    {
        try
        {
            await EjecutarAsync("ROLLBACK;", CancellationToken.None);
        }
        catch (SqliteException)
        {
            // Sin transacción activa (p. ej. ya confirmada o revertida por el
            // motor): no hay nada que revertir.
        }
    }
}
