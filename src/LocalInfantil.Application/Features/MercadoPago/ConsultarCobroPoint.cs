using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Consulta puntual de una order y aplica la transición local. Si el remoto
/// está processed, ejecuta la confirmación local idempotente (venta, pagos,
/// stock, caja, auditoría) con el snapshot durable y el mismo operacion_id:
/// reintentar nunca duplica. Fallo local tras MP → requiere_conciliacion,
/// nunca refund automático ni segundo cobro.
/// </summary>
public sealed class ConsultarCobroPoint
{
    private readonly IRepositorioMercadoPago _mp;
    private readonly IRepositorioVentas _ventas;
    private readonly ConfirmarVenta _confirmar;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public ConsultarCobroPoint(
        IRepositorioMercadoPago mp,
        IRepositorioVentas ventas,
        ConfirmarVenta confirmar,
        IMercadoPagoCredencialProvider credenciales,
        IMercadoPagoPointClient cliente,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj)
    {
        _mp = mp;
        _ventas = ventas;
        _confirmar = confirmar;
        _credenciales = credenciales;
        _cliente = cliente;
        _fabrica = fabrica;
        _reloj = reloj;
    }

    /// <summary>
    /// Consulta puntual en fases cortas: lee la saga, consulta la red SIN
    /// transacción abierta, persiste la transición con commit, y recién
    /// después confirma localmente (sesiones propias, sin anidamiento).
    /// </summary>
    public async Task<OperacionMpRegistrada> EjecutarAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        var operacion = await LeerOperacionAsync(operacionId, cancellationToken)
            ?? throw new InvalidOperationException("El cobro no existe.");
        if (TransicionesOperacionMp.EsTerminal(operacion.EstadoLocal))
            return operacion;
        if (operacion.OrderId is null)
        {
            // Timeout al crear: la order pudo existir remotamente. Reintentar
            // POST con la MISMA idempotency key devuelve la order existente
            // (replay) o la crea; nunca duplica.
            var recuperada = await ReintentarCreacionAsync(operacion, cancellationToken);
            if (recuperada is null)
                return (await MarcarPendienteVerificarAsync(operacionId, operacion.EstadoMp, cancellationToken))!;
            if (recuperada.EstadoLocal == EstadosLocalesMp.ProcesadaRemota)
                recuperada = await ConfirmarLocalAsync(recuperada, idSesion, cancellationToken);
            return recuperada;
        }

        var token = await _credenciales.ObtenerAsync(cancellationToken);
        if (token is null)
        {
            await TransicionarFrescoAsync(operacionId, EstadosLocalesMp.RequiereConciliacion, operacion.EstadoMp, error: "Hay un cobro de Mercado Pago pendiente de verificar.", ct: cancellationToken);
            return (await LeerOperacionAsync(operacionId, cancellationToken))!;
        }

        OrdenMp orden;
        try
        {
            orden = await _cliente.ObtenerOrdenAsync(token, operacion.OrderId, cancellationToken);
        }
        catch (ExcepcionMercadoPago ex)
        {
            await TransicionarFrescoAsync(operacionId, EstadosLocalesMp.RequiereConciliacion, operacion.EstadoMp, error: ex.Message, ct: cancellationToken);
            return (await LeerOperacionAsync(operacionId, cancellationToken))!;
        }
        catch (HttpRequestException)
        {
            await TransicionarFrescoAsync(operacionId, EstadosLocalesMp.RequiereConciliacion, operacion.EstadoMp, error: "Hay un cobro de Mercado Pago pendiente de verificar.", ct: cancellationToken);
            return (await LeerOperacionAsync(operacionId, cancellationToken))!;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TransicionarFrescoAsync(operacionId, operacion.EstadoLocal, operacion.EstadoMp, error: "Verificando el estado del cobro...", ct: cancellationToken);
            return (await LeerOperacionAsync(operacionId, cancellationToken))!;
        }

        var destino = TransicionesOperacionMp.MapearEstadoRemoto(operacion.EstadoLocal, orden.Estado) ?? operacion.EstadoLocal;
        if (destino == EstadosLocalesMp.ProcesadaRemota && operacion.EstadoLocal != EstadosLocalesMp.ProcesadaRemota)
            await TransicionarFrescoAsync(operacionId, EstadosLocalesMp.ProcesadaRemota, orden.Estado, ct: cancellationToken);
        else if (destino != operacion.EstadoLocal)
            await TransicionarFrescoAsync(operacionId, destino, orden.Estado, error: MensajeEstado(destino), ct: cancellationToken);

        operacion = (await LeerOperacionAsync(operacionId, cancellationToken))!;
        if (operacion.EstadoLocal == EstadosLocalesMp.ProcesadaRemota)
            operacion = await ConfirmarLocalAsync(operacion, idSesion, cancellationToken);
        return operacion;
    }

    /// <summary>
    /// Confirmación local en fases con sesiones cortas: nunca se anidan
    /// escrituras (ConfirmarVenta usa su propia transacción). Cada fase hace
    /// commit antes de la siguiente; reintentar es idempotente.
    /// </summary>
    private async Task<OperacionMpRegistrada> ConfirmarLocalAsync(OperacionMpRegistrada operacion, string idSesion, CancellationToken cancellationToken)
    {
        var snapshot = await LeerSnapshotAsync(operacion.Id, cancellationToken);

        await using (var s = await _fabrica.IniciarAsync(cancellationToken))
        {
            if (await _ventas.ExistePorOperacionAsync(s, operacion.OperacionId, cancellationToken))
            {
                var existente = await _ventas.ObtenerPorOperacionAsync(s, operacion.OperacionId, cancellationToken);
                await TransicionarAsync(s, operacion, EstadosLocalesMp.Confirmada, operacion.EstadoMp, ventaId: existente?.Id, ct: cancellationToken);
                await s.ConfirmarAsync(cancellationToken);
                return await LeerOperacionAsync(operacion.OperacionId, cancellationToken);
            }

            await TransicionarAsync(s, operacion, EstadosLocalesMp.ConfirmandoLocal, operacion.EstadoMp, ct: cancellationToken);
            await s.ConfirmarAsync(cancellationToken);
        }

        ResultadoConfirmacionVenta resultado;
        try
        {
            resultado = await _confirmar.EjecutarAsync(snapshot.Lineas, snapshot.Pagos, operacion.OperacionId, idSesion, cancellationToken);
        }
        catch (Exception ex) when (ex is not (SesionInvalidaException or UnauthorizedAccessException))
        {
            await TransicionarFrescoAsync(operacion.OperacionId, EstadosLocalesMp.RequiereConciliacion, operacion.EstadoMp, error: Sanitizar(ex.Message), ct: cancellationToken);
            return await LeerOperacionAsync(operacion.OperacionId, cancellationToken);
        }

        if (resultado is ResultadoConfirmacionVenta.PrecioCambiado)
        {
            await TransicionarFrescoAsync(operacion.OperacionId, EstadosLocalesMp.Fallida, operacion.EstadoMp, error: "El precio cambió antes de confirmar. Cancelá el cobro en la terminal.", ct: cancellationToken);
            return await LeerOperacionAsync(operacion.OperacionId, cancellationToken);
        }

        var ventaId = resultado is ResultadoConfirmacionVenta.Confirmada c ? c.VentaId : null;
        await TransicionarFrescoAsync(operacion.OperacionId, EstadosLocalesMp.Confirmada, operacion.EstadoMp, ventaId: ventaId, ct: cancellationToken);
        return await LeerOperacionAsync(operacion.OperacionId, cancellationToken);
    }

    private async Task<(IReadOnlyList<NuevaLineaVenta> Lineas, IReadOnlyList<NuevoPago> Pagos)> LeerSnapshotAsync(string mpId, CancellationToken ct)
    {
        await using var s = await _fabrica.IniciarLecturaAsync(ct);
        var lineas = await _mp.ListarLineasAsync(s, mpId, ct);
        var pagosPlan = await _mp.ListarPagosPlanificadosAsync(s, mpId, ct);
        return (lineas.Select(l => new NuevaLineaVenta(l.VarianteId, l.Cantidad, l.PrecioUnitarioMinor)).ToList(),
            pagosPlan.Select(p => new NuevoPago(p.Medio, p.ImporteMinor, p.RecibidoMinor)).ToList());
    }

    private async Task<OperacionMpRegistrada> LeerOperacionAsync(string operacionId, CancellationToken ct)
    {
        await using var s = await _fabrica.IniciarLecturaAsync(ct);
        return (await _mp.ObtenerOperacionAsync(s, operacionId, ct))!;
    }

    /// <summary>
    /// Reintenta la creación con la misma idempotency key persistida. Si la
    /// order ya existía, MP la devuelve (mismo order_id); si no, la crea.
    /// Devuelve null si sigue sin saberse (timeout/offline/error).
    /// </summary>
    private async Task<OperacionMpRegistrada?> ReintentarCreacionAsync(OperacionMpRegistrada operacion, CancellationToken ct)
    {
        var token = await _credenciales.ObtenerAsync(ct);
        if (token is null) return null;
        OrdenMp orden;
        try
        {
            await using var s = await _fabrica.IniciarLecturaAsync(ct);
            var saga = await _mp.ObtenerOperacionAsync(s, operacion.OperacionId, ct);
            if (saga is null || saga.OrderId is not null) return saga;
            orden = await _cliente.CrearOrdenAsync(token, saga.IdempotencyKey, saga.ExternalReference, saga.ImporteMinor, saga.TerminalId, $"Venta local {saga.OperacionId[..8]}", ct);
        }
        catch
        {
            return null;
        }

        var destino = orden.Estado == EstadosOrdenMp.Processed
            ? EstadosLocalesMp.ProcesadaRemota
            : EstadosLocalesMp.EsperandoTerminal;
        await TransicionarFrescoAsync(operacion.OperacionId, destino, orden.Estado, orderId: orden.Id, ct: ct);
        var actual = (await LeerOperacionAsync(operacion.OperacionId, ct))!;
        return actual;
    }

    private async Task<OperacionMpRegistrada?> MarcarPendienteVerificarAsync(string operacionId, string? estadoMp, CancellationToken ct)
    {
        await TransicionarFrescoAsync(operacionId, EstadosLocalesMp.RequiereConciliacion, estadoMp, error: "Hay un cobro de Mercado Pago pendiente de verificar.", ct: ct);
        return await LeerOperacionAsync(operacionId, ct);
    }

    /// <summary>Transiciona desde el estado real vigente (robusto ante reintentos).</summary>
    private async Task TransicionarFrescoAsync(string operacionId, string destino, string? estadoMp, string? error = null, string? ventaId = null, string? orderId = null, CancellationToken ct = default)
    {
        await using var s = await _fabrica.IniciarAsync(ct);
        var actual = await _mp.ObtenerOperacionAsync(s, operacionId, ct)
            ?? throw new InvalidOperationException("El cobro no existe.");
        if (!string.Equals(actual.EstadoLocal, destino, StringComparison.Ordinal))
            TransicionesOperacionMp.Exigir(actual.EstadoLocal, destino);
        await _mp.ActualizarEstadoAsync(s, actual.Id, destino, estadoMp ?? actual.EstadoMp, orderId ?? actual.OrderId, ventaId, error, _reloj.UtcNow().ToString("o"), ct);
        await s.ConfirmarAsync(ct);
    }

    private async Task TransicionarAsync(
        ISesionPersistencia sesion,
        OperacionMpRegistrada operacion,
        string destino,
        string? estadoMp,
        string? error = null,
        string? ventaId = null,
        CancellationToken ct = default)
    {
        if (!string.Equals(operacion.EstadoLocal, destino, StringComparison.Ordinal))
            TransicionesOperacionMp.Exigir(operacion.EstadoLocal, destino);
        await _mp.ActualizarEstadoAsync(sesion, operacion.Id, destino, estadoMp ?? operacion.EstadoMp, operacion.OrderId, ventaId, error, _reloj.UtcNow().ToString("o"), ct);
    }

    private static string? MensajeEstado(string destino) => destino switch
    {
        EstadosLocalesMp.Fallida => "El cobro fue rechazado. No se registró venta.",
        EstadosLocalesMp.Cancelada => "El cobro fue cancelado. No se registró venta.",
        EstadosLocalesMp.Expirada => "El cobro expiró. No se registró venta.",
        EstadosLocalesMp.RequiereConciliacion => "Hay un cobro de Mercado Pago pendiente de verificar.",
        _ => null,
    };

    private static string Sanitizar(string mensaje)
        => mensaje.Length <= 200 ? mensaje : mensaje[..200];
}
