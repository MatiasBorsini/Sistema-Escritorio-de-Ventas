using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Actualizaciones;

/// <summary>
/// Puerta de momento seguro: la instalación solo se permite con caja
/// cerrada, sin respaldo/restauración en curso y sin descarga en curso.
/// Nunca cierra caja automáticamente. Las operaciones financieras en curso
/// son transaccionales (rollback seguro); la puerta se revalida justo antes
/// de lanzar el instalador.
/// </summary>
public sealed class PuertaInstalacion
{
    private readonly IRepositorioCaja _caja;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IAlmacenActualizaciones _almacen;

    public PuertaInstalacion(
        IRepositorioCaja caja,
        IFabricaSesionesPersistencia fabrica,
        IAlmacenActualizaciones almacen)
    {
        _caja = caja;
        _fabrica = fabrica;
        _almacen = almacen;
    }

    public async Task<ResultadoInstalacionSegura> AdquirirAsync(string rutaInstalador, CancellationToken cancellationToken = default)
    {
        var sesion = await _fabrica.IniciarAsync(cancellationToken);
        try
        {
            // La sesión BEGIN IMMEDIATE funciona como puerta global temporal:
            // espera a la operación de escritura previa y bloquea nuevas
            // escrituras durante confirmación y lanzamiento del instalador.
            var abierta = await _caja.ObtenerAbiertaAsync(sesion, cancellationToken);
            if (abierta is not null)
                return await RechazarAsync(sesion, "Hay una actualización lista. Podés instalarla después de cerrar la caja.");

            if (_almacen.HayOperacionRespaldoEnCurso())
                return await RechazarAsync(sesion, "Hay un respaldo o restauración en curso. Esperá a que termine e intentá de nuevo.");

            return new ResultadoInstalacionSegura(true, null, rutaInstalador, sesion);
        }
        catch
        {
            await sesion.DisposeAsync();
            throw;
        }
    }

    private static async Task<ResultadoInstalacionSegura> RechazarAsync(ISesionPersistencia sesion, string motivo)
    {
        await sesion.DisposeAsync();
        return new ResultadoInstalacionSegura(false, motivo, null, null);
    }
}
