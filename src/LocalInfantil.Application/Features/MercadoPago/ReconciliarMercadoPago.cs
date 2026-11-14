using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Reconciliación puntual (arranque y manual): busca sagas y reembolsos no
/// terminales y los consulta contra Mercado Pago. Sin internet no inventa
/// estado: reporta pendientes de verificar. Bloquea solo lo que podría
/// duplicar/confundir la transacción (nuevos cobros y cierre con
/// pendientes), nunca todo el sistema.
/// </summary>
public sealed class ReconciliarMercadoPago
{
    private readonly IRepositorioMercadoPago _mp;
    private readonly ConsultarCobroPoint _consultar;
    private readonly ReembolsarMercadoPago? _reembolsar;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public ReconciliarMercadoPago(
        IRepositorioMercadoPago mp,
        ConsultarCobroPoint consultar,
        IMercadoPagoCredencialProvider credenciales,
        IMercadoPagoPointClient cliente,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj,
        ReembolsarMercadoPago? reembolsar = null)
    {
        _mp = mp;
        _consultar = consultar;
        _credenciales = credenciales;
        _cliente = cliente;
        _fabrica = fabrica;
        _reloj = reloj;
        _reembolsar = reembolsar;
    }

    public sealed record ResumenReconciliacion(
        int OperacionesRevisadas,
        int ReembolsosRevisados,
        int PendientesDeVerificar,
        bool HayInternet);

    public async Task<ResumenReconciliacion> EjecutarAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OperacionMpRegistrada> operaciones;
        IReadOnlyList<ReembolsoMpRegistrado> reembolsos;
        await using (var listado = await _fabrica.IniciarLecturaAsync(cancellationToken))
        {
            operaciones = await _mp.ListarNoTerminalesAsync(listado, cancellationToken);
            reembolsos = await _mp.ListarReembolsosNoTerminalesAsync(listado, cancellationToken);
        }
        if (operaciones.Count == 0 && reembolsos.Count == 0)
            return new ResumenReconciliacion(0, 0, 0, true);

        var hayInternet = await HayConexionAsync(cancellationToken);
        int revisadas = 0;
        if (hayInternet && idSesion is not null)
        {
            foreach (var op in operaciones)
            {
                try { await _consultar.EjecutarAsync(op.OperacionId, idSesion, cancellationToken); revisadas++; }
                catch { }
            }
            foreach (var r in reembolsos)
            {
                try { await ReconciliarReembolsoAsync(r, idSesion, cancellationToken); }
                catch { }
            }
        }

        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var pendientes = (await _mp.ListarNoTerminalesAsync(lectura, cancellationToken)).Count
            + (await _mp.ListarReembolsosNoTerminalesAsync(lectura, cancellationToken)).Count;
        return new ResumenReconciliacion(revisadas, reembolsos.Count, pendientes, hayInternet);
    }

    public async Task<bool> HayPendientesEnSesionAsync(string cajaSesionId, CancellationToken cancellationToken = default)
    {
        await using var sesion = await _fabrica.IniciarLecturaAsync(cancellationToken);
        return (await _mp.ListarPendientesPorSesionAsync(sesion, cajaSesionId, cancellationToken)).Count > 0;
    }

    private async Task<bool> HayConexionAsync(CancellationToken cancellationToken)
    {
        var token = await _credenciales.ObtenerAsync(cancellationToken);
        if (token is null) return false;
        try
        {
            await _cliente.ListarTerminalesAsync(token, 1, 0, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReconciliarReembolsoAsync(ReembolsoMpRegistrado reembolso, string idSesion, CancellationToken cancellationToken)
    {
        var token = await _credenciales.ObtenerAsync(cancellationToken);
        if (token is null) return;
        OrdenMp orden;
        try
        {
            orden = await _cliente.ObtenerOrdenAsync(token, reembolso.OrderId, cancellationToken);
        }
        catch
        {
            return;
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var actual = await _mp.ObtenerReembolsoAsync(sesion, reembolso.ReembolsoOperacionId, cancellationToken);
        if (actual is null || actual.EstadoLocal is EstadosLocalesReembolsoMp.ConfirmadaLocal or EstadosLocalesReembolsoMp.Fallida)
            return;
        if (orden.Estado == EstadosOrdenMp.Refunded)
        {
            TransicionesOperacionMp.ExigirReembolso(actual.EstadoLocal, EstadosLocalesReembolsoMp.ProcesadaRemota);
            await _mp.ActualizarReembolsoAsync(sesion, actual.Id, EstadosLocalesReembolsoMp.ProcesadaRemota, orden.Estado, null, _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
        }
        else if (orden.Estado is EstadosOrdenMp.Failed or EstadosOrdenMp.Canceled or EstadosOrdenMp.Expired)
        {
            TransicionesOperacionMp.ExigirReembolso(actual.EstadoLocal, EstadosLocalesReembolsoMp.Fallida);
            await _mp.ActualizarReembolsoAsync(sesion, actual.Id, EstadosLocalesReembolsoMp.Fallida, orden.Estado, "El reembolso no se concretó.", _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
        }
        else if (_reembolsar is not null)
        {
            await sesion.ConfirmarAsync(cancellationToken);
            await _reembolsar.ReintentarAsync(actual.ReembolsoOperacionId, cancellationToken);
        }
    }
}
