namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// La base está ocupada por otra operación (bloqueo SQLITE_BUSY/SQLITE_LOCKED
/// agotado el tiempo de espera). No se reintenta automáticamente: repetir una
/// escritura podría duplicarla si la primera había llegado a confirmarse.
/// El llamador decide con su identificador de operación. La excepción interna
/// conserva el detalle técnico.
/// </summary>
public sealed class AlmacenamientoOcupadoException : Exception
{
    public AlmacenamientoOcupadoException(string mensaje, Exception excepcionInterna)
        : base(mensaje, excepcionInterna)
    {
    }
}
