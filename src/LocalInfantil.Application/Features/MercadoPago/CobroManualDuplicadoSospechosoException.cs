using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Se detectaron uno o más cobros manuales declarados con el mismo importe
/// (y terminal) dentro de la ventana reciente. NO se descarta ni se vincula
/// nada: se exige una confirmación humana explícita antes de registrar otro.
/// </summary>
public sealed class CobroManualDuplicadoSospechosoException : Exception
{
    public IReadOnlyList<CobroManualMpRegistrado> Candidatos { get; }

    public CobroManualDuplicadoSospechosoException(IReadOnlyList<CobroManualMpRegistrado> candidatos)
        : base("Ya hay un cobro manual de Point por el mismo importe hace poco. Confirmá si es una segunda venta o un duplicado.")
    {
        Candidatos = candidatos;
    }
}
