namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Opciones de autenticación. El retardo ante fallos frena la prueba masiva
/// de contraseñas sin bloquear cuentas (un bloqueo permanente podría paralizar
/// el local). No hay política de caducidad de sesión ni de complejidad:
/// pendientes de decisión, no inventadas aquí (solo se exige no vacía).
/// </summary>
public sealed class OpcionesAutenticacion
{
    public const int RetrasoFalloMsPorDefecto = 500;

    public OpcionesAutenticacion(int retrasoFalloMs = RetrasoFalloMsPorDefecto)
    {
        if (retrasoFalloMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retrasoFalloMs), "El retardo no puede ser negativo.");
        }

        RetrasoFalloMs = retrasoFalloMs;
    }

    public int RetrasoFalloMs { get; }
}
