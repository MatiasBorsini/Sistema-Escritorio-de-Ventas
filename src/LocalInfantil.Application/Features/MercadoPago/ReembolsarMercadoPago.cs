using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Reembolso durable (saga): persiste la intención ANTES de llamar a
/// POST /v1/orders/{id}/refund con idempotency key propia. Timeout o crash
/// → requiere_conciliacion; nunca dos reembolsos por un timeout. El flujo
/// F06 confirma el efecto local y marca confirmada_local.
/// </summary>
public sealed class ReembolsarMercadoPago
{
    private readonly IRepositorioMercadoPago _mp;
    private readonly IRepositorioVentas _ventas;
    private readonly IRepositorioPagos _pagos;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public ReembolsarMercadoPago(
        IRepositorioMercadoPago mp,
        IRepositorioVentas ventas,
        IRepositorioPagos pagos,
        IMercadoPagoCredencialProvider credenciales,
        IMercadoPagoPointClient cliente,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _mp = mp;
        _ventas = ventas;
        _pagos = pagos;
        _credenciales = credenciales;
        _cliente = cliente;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(
        string ventaId,
        string pagoId,
        long importeMinor,
        string permisoFinanciero,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, permisoFinanciero, cancellationToken);
        if (importeMinor <= 0) throw new ArgumentException("Importe inválido.", nameof(importeMinor));

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var venta = await _ventas.ObtenerPorIdAsync(sesion, ventaId, cancellationToken)
            ?? throw new InvalidOperationException("La venta no existe.");
        if (venta.Estado != "confirmada")
            throw new InvalidOperationException("Solo se reintegra sobre ventas confirmadas.");
        var pago = (await _pagos.ListarPorVentaAsync(sesion, ventaId, cancellationToken)).FirstOrDefault(p => p.Id == pagoId)
            ?? throw new InvalidOperationException("El pago no existe.");
        if (pago.Medio != Contracts.MediosPago.MercadoPago)
            throw new InvalidOperationException("El pago no es un cobro de Mercado Pago de esta venta.");

        var pendientes = await _mp.ListarReembolsosNoTerminalesAsync(sesion, cancellationToken);
        if (pendientes.Any(r => r.PagoId == pagoId))
            throw new InvalidOperationException("Ya hay un reembolso en curso para este cobro.");

        var yaReembolsado = await SumarReembolsadoAsync(sesion, pagoId, cancellationToken);
        if (yaReembolsado + importeMinor > pago.ImporteMinor)
            throw new InvalidOperationException("El reembolso supera el cobro original.");

        var saga = await BuscarSagaConfirmadaAsync(sesion, ventaId, cancellationToken)
            ?? throw new InvalidOperationException("No hay cobro Point confirmado para esta venta.");
        if (saga.OrderId is null)
            throw new InvalidOperationException("El cobro no tiene order asociada.");

        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");

        var reembolsoOperacionId = _ids.NuevoId();
        var key = Guid.NewGuid().ToString("N");
        var fecha = _reloj.UtcNow().ToString("o");
        await _mp.InsertarReembolsoAsync(sesion, saga.Id, ventaId, pagoId, reembolsoOperacionId, key, saga.OrderId, importeMinor, fecha, cancellationToken);

        try
        {
            var total = importeMinor == pago.ImporteMinor - yaReembolsado;
            var orden = await _cliente.ReembolsarOrdenAsync(token, key, saga.OrderId, total ? null : importeMinor, cancellationToken);
            var estado = orden.Estado == Contracts.EstadosOrdenMp.Refunded
                ? Contracts.EstadosLocalesReembolsoMp.ProcesadaRemota
                : Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion;
            TransicionesOperacionMp.ExigirReembolso(Contracts.EstadosLocalesReembolsoMp.Solicitada, estado);
            var reembolso = await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken);
            await _mp.ActualizarReembolsoAsync(sesion, reembolso!.Id, estado, orden.Estado,
                estado == Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion ? "Reembolso pendiente de verificar." : null, fecha, cancellationToken);
        }
        catch (Abstractions.ExcepcionMercadoPago ex)
        {
            var reembolso = await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken);
            await _mp.ActualizarReembolsoAsync(sesion, reembolso!.Id, Contracts.EstadosLocalesReembolsoMp.Fallida, null, ex.Message, fecha, cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            var reembolso = await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken);
            await _mp.ActualizarReembolsoAsync(sesion, reembolso!.Id, Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion, null, "Reembolso pendiente de verificar.", fecha, cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException("Mercado Pago no está disponible. El reembolso quedó pendiente de verificar.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var reembolso = await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken);
            await _mp.ActualizarReembolsoAsync(sesion, reembolso!.Id, Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion, null, "Reembolso pendiente de verificar.", fecha, cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException("Verificando el estado del reembolso...");
        }

        await sesion.ConfirmarAsync(cancellationToken);
        return reembolsoOperacionId;
    }

    /// <summary>
    /// Reintenta un reembolso incierto con su MISMA idempotency key: si MP
    /// ya lo procesó, devuelve el resultado original sin duplicar; si no,
    /// lo procesa. Sin autorización (el intento original ya fue autorizado;
    /// no crea operación nueva). Uso interno de la reconciliación.
    /// </summary>
    public async Task ReintentarAsync(string reembolsoOperacionId, CancellationToken cancellationToken = default)
    {
        Contracts.ReembolsoMpRegistrado reembolso;
        await using (var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken))
        {
            reembolso = await _mp.ObtenerReembolsoAsync(lectura, reembolsoOperacionId, cancellationToken)
                ?? throw new InvalidOperationException("El reembolso no existe.");
        }
        if (reembolso.EstadoLocal is Contracts.EstadosLocalesReembolsoMp.ConfirmadaLocal or Contracts.EstadosLocalesReembolsoMp.Fallida)
            return;

        var token = await _credenciales.ObtenerAsync(cancellationToken);
        if (token is null) return;

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var pagos = await _pagos.ListarPorVentaAsync(sesion, reembolso.VentaId, cancellationToken);
        var pago = pagos.FirstOrDefault(p => p.Id == reembolso.PagoId);
        var fecha = _reloj.UtcNow().ToString("o");
        var total = pago is not null && reembolso.ImporteMinor == pago.ImporteMinor;

        try
        {
            var orden = await _cliente.ReembolsarOrdenAsync(token, reembolso.IdempotencyKey, reembolso.OrderId, total ? null : reembolso.ImporteMinor, cancellationToken);
            var actual = (await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken))!;
            if (orden.Estado == Contracts.EstadosOrdenMp.Refunded)
            {
                if (actual.EstadoLocal != Contracts.EstadosLocalesReembolsoMp.ProcesadaRemota)
                {
                    TransicionesOperacionMp.ExigirReembolso(actual.EstadoLocal, Contracts.EstadosLocalesReembolsoMp.ProcesadaRemota);
                    await _mp.ActualizarReembolsoAsync(sesion, actual.Id, Contracts.EstadosLocalesReembolsoMp.ProcesadaRemota, orden.Estado, null, fecha, cancellationToken);
                }
            }
            else if (actual.EstadoLocal != Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion)
            {
                TransicionesOperacionMp.ExigirReembolso(actual.EstadoLocal, Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion);
                await _mp.ActualizarReembolsoAsync(sesion, actual.Id, Contracts.EstadosLocalesReembolsoMp.RequiereConciliacion, orden.Estado, "Reembolso pendiente de verificar.", fecha, cancellationToken);
            }
            await sesion.ConfirmarAsync(cancellationToken);
        }
        catch (Abstractions.ExcepcionMercadoPago ex)
        {
            var actual = (await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken))!;
            TransicionesOperacionMp.ExigirReembolso(actual.EstadoLocal, Contracts.EstadosLocalesReembolsoMp.Fallida);
            await _mp.ActualizarReembolsoAsync(sesion, actual.Id, Contracts.EstadosLocalesReembolsoMp.Fallida, null, ex.Message, fecha, cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
        }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    public async Task MarcarConfirmadoLocalAsync(string reembolsoOperacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var reembolso = await _mp.ObtenerReembolsoAsync(sesion, reembolsoOperacionId, cancellationToken)
            ?? throw new InvalidOperationException("El reembolso no existe.");
        TransicionesOperacionMp.ExigirReembolso(reembolso.EstadoLocal, Contracts.EstadosLocalesReembolsoMp.ConfirmadaLocal);
        await _mp.ActualizarReembolsoAsync(sesion, reembolso.Id, Contracts.EstadosLocalesReembolsoMp.ConfirmadaLocal, reembolso.EstadoMp, null, _reloj.UtcNow().ToString("o"), cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }

    private async Task<long> SumarReembolsadoAsync(ISesionPersistencia sesion, string pagoId, CancellationToken cancellationToken)
    {
        var todos = await _mp.ListarReembolsosPorPagoAsync(sesion, pagoId, cancellationToken);
        return todos.Where(r => r.EstadoLocal != Contracts.EstadosLocalesReembolsoMp.Fallida).Sum(r => r.ImporteMinor);
    }

    private async Task<Contracts.OperacionMpRegistrada?> BuscarSagaConfirmadaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken)
    {
        var saga = await _mp.ObtenerOperacionPorVentaAsync(sesion, ventaId, cancellationToken);
        return saga?.EstadoLocal == Contracts.EstadosLocalesMp.Confirmada ? saga : null;
    }
}
