namespace LocalInfantil.Application.Abstractions;

/// <summary>La sesión no existe, se cerró o su usuario fue desactivado.</summary>
public sealed class SesionInvalidaException : Exception
{
    public SesionInvalidaException()
        : base("La sesión no es válida o el usuario fue desactivado.")
    {
    }
}
