namespace LocalInfantil.Application.Abstractions;

/// <summary>El archivo no es un respaldo válido (corrupto, incompatible o no reconocido).</summary>
public sealed class RespaldoInvalidoException : Exception
{
    public RespaldoInvalidoException(string mensaje, Exception? excepcionInterna = null)
        : base(mensaje, excepcionInterna)
    {
    }
}
