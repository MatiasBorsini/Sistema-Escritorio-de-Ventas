namespace LocalInfantil.Application.Abstractions;

/// <summary>El respaldo no pudo generarse (origen, destino o escritura fallaron).</summary>
public sealed class RespaldoFallidoException : Exception
{
    public RespaldoFallidoException(string mensaje, Exception? excepcionInterna = null)
        : base(mensaje, excepcionInterna)
    {
    }
}
