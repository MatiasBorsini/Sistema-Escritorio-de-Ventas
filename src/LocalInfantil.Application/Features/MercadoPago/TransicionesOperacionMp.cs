using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Máquina de estados explícita de la saga F12. Toda transición pasa por
/// aquí; lo no listado es inválido. Estados desconocidos del remoto nunca
/// se tratan como éxito ni como fallo: van a requiere_conciliacion.
/// </summary>
public static class TransicionesOperacionMp
{
    private static readonly Dictionary<string, HashSet<string>> Permitidas = new(StringComparer.Ordinal)
    {
        [EstadosLocalesMp.Preparada] = new(StringComparer.Ordinal) { EstadosLocalesMp.CreandoOrder, EstadosLocalesMp.Fallida },
        [EstadosLocalesMp.CreandoOrder] = new(StringComparer.Ordinal) { EstadosLocalesMp.EsperandoTerminal, EstadosLocalesMp.Fallida, EstadosLocalesMp.RequiereConciliacion },
        [EstadosLocalesMp.EsperandoTerminal] = new(StringComparer.Ordinal) { EstadosLocalesMp.EsperandoTerminal, EstadosLocalesMp.ProcesadaRemota, EstadosLocalesMp.Cancelada, EstadosLocalesMp.Expirada, EstadosLocalesMp.Fallida, EstadosLocalesMp.RequiereConciliacion },
        [EstadosLocalesMp.ProcesadaRemota] = new(StringComparer.Ordinal) { EstadosLocalesMp.ConfirmandoLocal, EstadosLocalesMp.Confirmada, EstadosLocalesMp.RequiereConciliacion },
        [EstadosLocalesMp.ConfirmandoLocal] = new(StringComparer.Ordinal) { EstadosLocalesMp.Confirmada, EstadosLocalesMp.RequiereConciliacion },
        [EstadosLocalesMp.RequiereConciliacion] = new(StringComparer.Ordinal) { EstadosLocalesMp.EsperandoTerminal, EstadosLocalesMp.ProcesadaRemota, EstadosLocalesMp.ConfirmandoLocal, EstadosLocalesMp.Confirmada, EstadosLocalesMp.Cancelada, EstadosLocalesMp.Expirada, EstadosLocalesMp.Fallida, EstadosLocalesMp.RequiereConciliacion },
        [EstadosLocalesMp.Confirmada] = new(StringComparer.Ordinal) { },
        [EstadosLocalesMp.Fallida] = new(StringComparer.Ordinal) { },
        [EstadosLocalesMp.Cancelada] = new(StringComparer.Ordinal) { },
        [EstadosLocalesMp.Expirada] = new(StringComparer.Ordinal) { },
    };

    public static bool EsPermitida(string desde, string hacia)
        => Permitidas.TryGetValue(desde, out var destinos) && destinos.Contains(hacia);

    public static void Exigir(string desde, string hacia)
    {
        if (!EsPermitida(desde, hacia))
            throw new InvalidOperationException($"Transición de cobro inválida: {desde} → {hacia}.");
    }

    /// <summary>
    /// Mapea estado remoto → transición local. Devuelve null si no hay cambio.
    /// Estados desconocidos → requiere_conciliacion (nunca éxito ni fallo).
    /// </summary>
    public static string? MapearEstadoRemoto(string estadoLocalActual, string estadoRemoto) => estadoRemoto switch
    {
        EstadosOrdenMp.Created or EstadosOrdenMp.AtTerminal => estadoLocalActual == EstadosLocalesMp.EsperandoTerminal || estadoLocalActual == EstadosLocalesMp.RequiereConciliacion
            ? EstadosLocalesMp.EsperandoTerminal : null,
        EstadosOrdenMp.Processed => EstadosLocalesMp.ProcesadaRemota,
        EstadosOrdenMp.Failed => EstadosLocalesMp.Fallida,
        EstadosOrdenMp.Expired => EstadosLocalesMp.Expirada,
        EstadosOrdenMp.Canceled => EstadosLocalesMp.Cancelada,
        EstadosOrdenMp.Refunded => null,
        _ => EstadosLocalesMp.RequiereConciliacion,
    };

    public static bool EsTerminal(string estadoLocal) => estadoLocal is
        EstadosLocalesMp.Confirmada or EstadosLocalesMp.Fallida or EstadosLocalesMp.Cancelada or EstadosLocalesMp.Expirada;

    private static readonly Dictionary<string, HashSet<string>> PermitidasReembolso = new(StringComparer.Ordinal)
    {
        [EstadosLocalesReembolsoMp.Solicitada] = new(StringComparer.Ordinal) { EstadosLocalesReembolsoMp.ProcesadaRemota, EstadosLocalesReembolsoMp.Fallida, EstadosLocalesReembolsoMp.RequiereConciliacion },
        [EstadosLocalesReembolsoMp.ProcesadaRemota] = new(StringComparer.Ordinal) { EstadosLocalesReembolsoMp.ConfirmadaLocal, EstadosLocalesReembolsoMp.RequiereConciliacion },
        [EstadosLocalesReembolsoMp.RequiereConciliacion] = new(StringComparer.Ordinal) { EstadosLocalesReembolsoMp.ProcesadaRemota, EstadosLocalesReembolsoMp.ConfirmadaLocal, EstadosLocalesReembolsoMp.Fallida, EstadosLocalesReembolsoMp.RequiereConciliacion },
        [EstadosLocalesReembolsoMp.ConfirmadaLocal] = new(StringComparer.Ordinal) { },
        [EstadosLocalesReembolsoMp.Fallida] = new(StringComparer.Ordinal) { },
    };

    public static void ExigirReembolso(string desde, string hacia)
    {
        if (!PermitidasReembolso.TryGetValue(desde, out var destinos) || !destinos.Contains(hacia))
            throw new InvalidOperationException($"Transición de reembolso inválida: {desde} → {hacia}.");
    }
}
