using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Cancela un cobro antes de processed vía POST /v1/orders/{id}/cancel.
/// No cambia a cancelada sin respuesta confiable. Si ya está processed,
/// rechaza y redirige a devolución. La key es determinística por saga
/// ("cancel-" + idempotency key persistida): reintentar la MISMA
/// cancelación tras timeout no crea una intención nueva (contrato oficial:
/// X-Idempotency-Key requerida en cancel).
/// </summary>
public sealed class CancelarCobroPoint
{
    private readonly IRepositorioMercadoPago _mp;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public CancelarCobroPoint(
        IRepositorioMercadoPago mp,
        IMercadoPagoCredencialProvider credenciales,
        IMercadoPagoPointClient cliente,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj)
    {
        _mp = mp;
        _credenciales = credenciales;
        _cliente = cliente;
        _fabrica = fabrica;
        _reloj = reloj;
    }

    public static string ClaveCancelacion(string idempotencyKey) => $"cancel-{idempotencyKey}";

    public async Task<OperacionMpRegistrada> EjecutarAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var operacion = await _mp.ObtenerOperacionAsync(sesion, operacionId, cancellationToken)
            ?? throw new InvalidOperationException("El cobro no existe.");
        if (TransicionesOperacionMp.EsTerminal(operacion.EstadoLocal))
            return operacion;
        if (operacion.EstadoLocal is EstadosLocalesMp.ProcesadaRemota or EstadosLocalesMp.ConfirmandoLocal or EstadosLocalesMp.Confirmada)
            throw new InvalidOperationException("El cobro ya fue procesado. Usá la devolución si corresponde.");
        if (operacion.OrderId is null)
        {
            TransicionesOperacionMp.Exigir(operacion.EstadoLocal, EstadosLocalesMp.Cancelada);
            await _mp.ActualizarEstadoAsync(sesion, operacion.Id, EstadosLocalesMp.Cancelada, operacion.EstadoMp, operacion.OrderId, null, null, _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            return (await _mp.ObtenerOperacionAsync(sesion, operacionId, cancellationToken))!;
        }

        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");

        var clave = ClaveCancelacion(operacion.IdempotencyKey);
        OrdenMp orden;
        try
        {
            var enTerminal = operacion.EstadoMp == EstadosOrdenMp.AtTerminal;
            orden = await _cliente.CancelarOrdenAsync(token, clave, operacion.OrderId, enTerminal, cancellationToken);
        }
        catch (ExcepcionMercadoPago ex) when (ex.Codigo == "order_already_canceled")
        {
            await TransicionarAsync(sesion, operacion, EstadosLocalesMp.Cancelada, EstadosOrdenMp.Canceled, cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            return (await _mp.ObtenerOperacionAsync(sesion, operacionId, cancellationToken))!;
        }
        catch (ExcepcionMercadoPago ex) when (ex.Codigo is "idempotency_key_already_used" or "cannot_cancel_order")
        {
            return await ResolverPorConsultaAsync(sesion, token, operacion, operacionId, cancellationToken);
        }
        catch (ExcepcionMercadoPago ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Verificando el estado del cobro...");
        }

        var destino = MapearDestino(orden.Estado);
        await TransicionarAsync(sesion, operacion, destino, orden.Estado, cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return (await _mp.ObtenerOperacionAsync(sesion, operacionId, cancellationToken))!;
    }

    private static string MapearDestino(string estadoRemoto) => estadoRemoto switch
    {
        EstadosOrdenMp.Canceled => EstadosLocalesMp.Cancelada,
        EstadosOrdenMp.Processed => throw new InvalidOperationException("El cobro ya fue procesado. Usá la devolución si corresponde."),
        EstadosOrdenMp.Expired => EstadosLocalesMp.Expirada,
        _ => EstadosLocalesMp.RequiereConciliacion,
    };

    private async Task TransicionarAsync(ISesionPersistencia sesion, OperacionMpRegistrada operacion, string destino, string? estadoMp, CancellationToken ct)
    {
        TransicionesOperacionMp.Exigir(operacion.EstadoLocal, destino);
        await _mp.ActualizarEstadoAsync(sesion, operacion.Id, destino, estadoMp ?? operacion.EstadoMp, operacion.OrderId, null,
            destino == EstadosLocalesMp.RequiereConciliacion ? "Hay un cobro de Mercado Pago pendiente de verificar." : null,
            _reloj.UtcNow().ToString("o"), ct);
    }

    /// <summary>
    /// Resuelve por GET cuando la cancelación no fue concluyente: nunca
    /// inventa keys nuevas en loop; el estado remoto manda.
    /// </summary>
    private async Task<OperacionMpRegistrada> ResolverPorConsultaAsync(ISesionPersistencia sesion, string token, OperacionMpRegistrada operacion, string operacionId, CancellationToken ct)
    {
        OrdenMp orden;
        try
        {
            orden = await _cliente.ObtenerOrdenAsync(token, operacion.OrderId!, ct);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Verificando el estado del cobro...");
        }
        catch (ExcepcionMercadoPago ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        var destino = MapearDestino(orden.Estado);
        await TransicionarAsync(sesion, operacion, destino, orden.Estado, ct);
        await sesion.ConfirmarAsync(ct);
        return (await _mp.ObtenerOperacionAsync(sesion, operacionId, ct))!;
    }
}
