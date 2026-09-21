namespace LocalInfantil.Application.Contracts;

/// <summary>
/// Expone el resultado técnico de la comprobación sin asociarlo a datos comerciales.
/// </summary>
public sealed record ResultadoComprobacionPersistencia(
    bool EscrituraCorrecta,
    bool LecturaCorrecta,
    bool DatoCoincide,
    string? ValorRecuperado,
    string? MensajeError)
{
    public bool Correcta => EscrituraCorrecta && LecturaCorrecta && DatoCoincide && MensajeError is null;
}
