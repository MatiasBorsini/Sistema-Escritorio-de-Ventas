namespace LocalInfantil.Application.Abstractions;

/// <summary>Nombre de usuario o contraseña inválidos. El mensaje no revela cuál.</summary>
public sealed class CredencialesInvalidasException : Exception
{
    public CredencialesInvalidasException()
        : base("Nombre de usuario o contraseña inválidos.")
    {
    }
}
