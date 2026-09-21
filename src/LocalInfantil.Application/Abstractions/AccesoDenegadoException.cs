namespace LocalInfantil.Application.Abstractions;

/// <summary>La sesión es válida pero el permiso no está concedido.</summary>
public sealed class AccesoDenegadoException : Exception
{
    public AccesoDenegadoException(string permiso)
        : base($"Acceso denegado para el permiso '{permiso}'.")
    {
    }
}
