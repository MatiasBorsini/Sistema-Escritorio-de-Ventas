using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Cambios;

public sealed class GenerarTicketCambio(IAutorizador autorizador,IFabricaSesionesPersistencia fabrica,IRepositorioCambios cambios)
{
    public async Task<DocumentoTicketCambio?> EjecutarAsync(string cambioId,string? idSesion,CancellationToken ct=default)
    {
        if(idSesion is null)throw new SesionInvalidaException();
        await autorizador.AutorizarAsync(idSesion,PermisosCambios.Consultar,ct);
        await using var lectura=await fabrica.IniciarLecturaAsync(ct);
        return await cambios.ObtenerTicketAsync(lectura,cambioId,ct);
    }
}
