using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.CuentaCorriente;

public sealed class GenerarTicketAbono(IAutorizador auth, IFabricaSesionesPersistencia fabrica, IRepositorioCuentaCorriente cuenta)
{
    public async Task<DocumentoTicketAbono?> EjecutarAsync(string abonoId, string? idSesion, CancellationToken ct = default)
    {
        await auth.AutorizarAsync(ConsultasCuentaCorriente.ExigirSesion(idSesion), PermisosCuentaCorriente.Consultar, ct);
        await using var read = await fabrica.IniciarLecturaAsync(ct);
        return await cuenta.ObtenerTicketAbonoAsync(read, abonoId, ct);
    }
}
