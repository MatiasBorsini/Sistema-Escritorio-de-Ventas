using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Persistencia;

/// <summary>
/// Coordina una escritura y lectura técnica para comprobar la persistencia local del prototipo.
/// </summary>
public sealed class ComprobarPersistenciaLocal
{
    public const string ClaveTecnica = "comprobacion_persistencia_prototipo";

    private readonly IPersistenciaTecnicaDePrueba _persistencia;

    public ComprobarPersistenciaLocal(IPersistenciaTecnicaDePrueba persistencia)
    {
        _persistencia = persistencia ?? throw new ArgumentNullException(nameof(persistencia));
    }

    public async Task<ResultadoComprobacionPersistencia> EjecutarAsync(CancellationToken cancellationToken = default)
    {
        var valorEnviado = $"comprobacion-{Guid.NewGuid():N}";
        var escrituraCorrecta = false;
        var lecturaCorrecta = false;
        var datoCoincide = false;
        string? valorRecuperado = null;

        try
        {
            await _persistencia.InicializarAsync(cancellationToken);
            await _persistencia.GuardarAsync(ClaveTecnica, valorEnviado, cancellationToken);
            escrituraCorrecta = true;
            valorRecuperado = await _persistencia.LeerAsync(ClaveTecnica, cancellationToken);
            lecturaCorrecta = valorRecuperado is not null;
            datoCoincide = string.Equals(valorEnviado, valorRecuperado, StringComparison.Ordinal);

            return new ResultadoComprobacionPersistencia(
                EscrituraCorrecta: escrituraCorrecta,
                LecturaCorrecta: lecturaCorrecta,
                DatoCoincide: datoCoincide,
                ValorRecuperado: valorRecuperado,
                MensajeError: null);
        }
        catch (Exception exception)
        {
            return new ResultadoComprobacionPersistencia(
                EscrituraCorrecta: escrituraCorrecta,
                LecturaCorrecta: lecturaCorrecta,
                DatoCoincide: datoCoincide,
                ValorRecuperado: valorRecuperado,
                MensajeError: $"No se pudo completar la comprobación local: {exception.Message}");
        }
    }
}
