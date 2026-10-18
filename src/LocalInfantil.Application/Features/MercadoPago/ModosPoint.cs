namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Modalidad operativa LOCAL de Mercado Pago Point, elegida por la Dueña y
/// persistida en la configuración no secreta. Es independiente del modo
/// físico de la terminal (ModoOperativo PDV/STANDALONE):
/// - integrado: LocalInfantil crea la order y espera la aprobación remota.
/// - manual: la empleada cobra en el Point y registra la aprobación observada.
/// - desactivado: Point no se ofrece en el POS.
/// </summary>
public static class ModosPoint
{
    public const string Integrado = "integrado";
    public const string Manual = "manual";
    public const string Desactivado = "desactivado";

    public static readonly IReadOnlyList<string> Todos = new[] { Integrado, Manual, Desactivado };

    public static bool EsValido(string? modo)
        => modo is Integrado or Manual or Desactivado;

    /// <summary>Devuelve el modo válido o cadena vacía si no lo es.</summary>
    public static string Normalizar(string? modo)
        => EsValido(modo) ? modo! : string.Empty;

    /// <summary>
    /// Resuelve el modo efectivo. Si hay una elección guardada válida se
    /// respeta. Si no: una instalación con terminal ya configurada queda en
    /// integrado (compatibilidad con la configuración anterior a los modos),
    /// y una sin configuración previa queda desactivada (nunca habilita el
    /// cobro automáticamente).
    /// </summary>
    public static string Resolver(string? modoGuardado, bool hayTerminalPrevia)
        => EsValido(modoGuardado)
            ? modoGuardado!
            : hayTerminalPrevia ? Integrado : Desactivado;

    public static string TextoHumano(string modo) => modo switch
    {
        Integrado => "Integrado (LocalInfantil cobra en el Point)",
        Manual => "Manual (se cobra en el Point y se registra)",
        Desactivado => "Desactivado",
        _ => modo,
    };
}
