using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Concilia filas del reporte de liquidación con las ventas/pagos/cobros del
/// local.
///
/// - Automática: solo con identidad remota inequívoca que el sistema ya
///   conservó al cobrar (EXTERNAL_REFERENCE 'LI-&lt;operacionId&gt;' u ORDER_ID
///   de la saga integrada; SOURCE_ID de un reembolso local). Nunca por monto
///   y fecha parecidos.
/// - Asistida: la Dueña elige la venta; se registra como vínculo humano
///   (tipo_match = asistido_humano). El sistema no inventa el vínculo.
///
/// Los movimientos que no son ventas (impuestos, saldos, transferencias) se
/// marcan como no_relacionados o requieren revisión: nunca como conciliados.
/// </summary>
public sealed class ConciliarLiquidacionMp
{
    private readonly IRepositorioLiquidacionesMp _liquidaciones;
    private readonly IRepositorioMercadoPago _mp;
    private readonly IRepositorioVentas _ventas;
    private readonly IRepositorioPagos _pagos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public ConciliarLiquidacionMp(
        IRepositorioLiquidacionesMp liquidaciones,
        IRepositorioMercadoPago mp,
        IRepositorioVentas ventas,
        IRepositorioPagos pagos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj)
    {
        _liquidaciones = liquidaciones;
        _mp = mp;
        _ventas = ventas;
        _pagos = pagos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _reloj = reloj;
    }

    /// <summary>
    /// Intenta conciliar una fila ya persistida dentro de la MISMA sesión de
    /// escritura de la importación. Devuelve el resultado o null si la fila no
    /// requiere conciliación automática (no es un cobro o ya estaba vinculada).
    /// </summary>
    public async Task<ResultadoConciliacionLiquidacionMp?> IntentarAutomaticaAsync(
        ISesionPersistencia sesion,
        LiquidacionMpRegistrada liquidacion,
        string creadoEn,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(liquidacion);
        if (!ClasificacionMovimientoMp.EsCobrable(liquidacion.Clasificacion))
        {
            var estado = liquidacion.Clasificacion == ClasificacionMovimientoMp.Desconocido
                ? EstadosConciliacionCobroManualMp.RequiereIntervencion
                : EstadosConciliacionCobroManualMp.NoRelacionada;
            var observacion = liquidacion.Clasificacion == ClasificacionMovimientoMp.Desconocido
                ? "Movimiento no reconocido: revisar manualmente."
                : "Movimiento no corresponde a una venta del local.";
            await _liquidaciones.ActualizarConciliacionLiquidacionAsync(sesion, liquidacion.Id, estado, null, null, null, null, observacion, cancellationToken);
            return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, estado, null, null, null, null, null, observacion);
        }

        if (await _liquidaciones.ExisteConciliacionDeLiquidacionAsync(sesion, liquidacion.Id, cancellationToken))
            return null;

        return liquidacion.Clasificacion switch
        {
            ClasificacionMovimientoMp.Devolucion or ClasificacionMovimientoMp.Contracargo =>
                await ConciliarReversoAsync(sesion, liquidacion, creadoEn, cancellationToken),
            _ => await ConciliarCobroAsync(sesion, liquidacion, creadoEn, cancellationToken)
        };
    }

    private async Task<ResultadoConciliacionLiquidacionMp> ConciliarCobroAsync(
        ISesionPersistencia sesion,
        LiquidacionMpRegistrada liquidacion,
        string creadoEn,
        CancellationToken cancellationToken)
    {
        // Integrado: EXTERNAL_REFERENCE = LI-<operacionId> (conservado al cobrar).
        OperacionMpRegistrada? operacion = null;
        string? tipoMatch = null;
        if (liquidacion.ExternalReference is { Length: > 3 } referencia &&
            referencia.StartsWith("LI-", StringComparison.Ordinal))
        {
            operacion = await _mp.ObtenerOperacionAsync(sesion, referencia[3..], cancellationToken);
            tipoMatch = operacion is not null ? TiposMatchConciliacionMp.ExactoExternalRef : null;
        }

        if (operacion is null && liquidacion.OrderId is { Length: > 0 } orden)
        {
            operacion = await _mp.ObtenerOperacionPorOrderAsync(sesion, orden, cancellationToken);
            if (operacion is not null) tipoMatch = TiposMatchConciliacionMp.ExactoOrder;
        }

        if (operacion?.VentaId is null)
            return await MarcarPendienteAsync(sesion, liquidacion, "Sin vínculo remoto inequívoco: requiere conciliación asistida.", cancellationToken);

        var venta = await _ventas.ObtenerPorIdAsync(sesion, operacion.VentaId, cancellationToken);
        if (venta is null)
            return await MarcarPendienteAsync(sesion, liquidacion, "La operación remota apunta a una venta inexistente: revisar.", cancellationToken);

        // Evita el doble reconocimiento: una misma venta no se vincula dos veces.
        if (await _liquidaciones.ExisteConciliacionPorVentaAsync(sesion, venta.Id, cancellationToken))
            return await MarcarRevisionAsync(
                sesion, liquidacion,
                "Posible doble reconocimiento: la venta ya está vinculada a otro movimiento del reporte. Revisar antes de conciliar.",
                cancellationToken);

        var pagoId = await BuscarPagoMercadoPagoAsync(sesion, venta.Id, cancellationToken);
        var declarado = ImporteDeclaradoMinor(liquidacion);
        var (estado, detalle) = Comparar(declarado, venta.TotalMinor);
        await _liquidaciones.ActualizarConciliacionLiquidacionAsync(sesion, liquidacion.Id, estado, venta.Id, pagoId, operacion.Id, null, detalle, cancellationToken);
        await _liquidaciones.InsertarConciliacionAsync(sesion, liquidacion.Id, venta.Id, pagoId, operacion.Id, null, tipoMatch ?? TiposMatchConciliacionMp.ExactoExternalRef, detalle, null, creadoEn, cancellationToken);
        return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, estado, tipoMatch, venta.Id, pagoId, operacion.Id, null, detalle);
    }

    private async Task<ResultadoConciliacionLiquidacionMp> ConciliarReversoAsync(
        ISesionPersistencia sesion,
        LiquidacionMpRegistrada liquidacion,
        string creadoEn,
        CancellationToken cancellationToken)
    {
        // Devolución/contracargo: se vincula al reembolso local por identidad
        // remota (SOURCE_ID = id de operación del reembolso). Nunca como venta
        // negativa ni como ingreso.
        ReembolsoMpRegistrado? reembolso = null;
        if (liquidacion.SourceId is { Length: > 0 } idReembolso)
            reembolso = await _mp.ObtenerReembolsoAsync(sesion, idReembolso, cancellationToken);

        if (reembolso is null)
            return await MarcarRevisionAsync(
                sesion, liquidacion,
                $"{(liquidacion.Clasificacion == ClasificacionMovimientoMp.Contracargo ? "Contracargo" : "Devolución")} sin reembolso local conocido: revisar antes de contabilizar.",
                cancellationToken);

        // Evita el doble reconocimiento del mismo reembolso.
        if (reembolso.OperacionMpId is { Length: > 0 } operacionReverso
            && await _liquidaciones.ExisteConciliacionPorOperacionAsync(sesion, operacionReverso, cancellationToken))
            return await MarcarRevisionAsync(
                sesion, liquidacion,
                "Posible doble reconocimiento: el reembolso ya está vinculado a otro movimiento del reporte. Revisar antes de conciliar.",
                cancellationToken);

        var declarado = ImporteDeclaradoMinor(liquidacion);
        var esperado = reembolso.ImporteMinor < 0 ? -reembolso.ImporteMinor : reembolso.ImporteMinor;
        var (estado, detalle) = Comparar(declarado, esperado);
        await _liquidaciones.ActualizarConciliacionLiquidacionAsync(sesion, liquidacion.Id, estado, reembolso.VentaId, reembolso.PagoId, reembolso.OperacionMpId, null, detalle, cancellationToken);
        await _liquidaciones.InsertarConciliacionAsync(sesion, liquidacion.Id, reembolso.VentaId, reembolso.PagoId, reembolso.OperacionMpId, null, TiposMatchConciliacionMp.ExactoPaymentId, detalle, null, creadoEn, cancellationToken);
        return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, estado, TiposMatchConciliacionMp.ExactoPaymentId, reembolso.VentaId, reembolso.PagoId, reembolso.OperacionMpId, null, detalle);
    }

    private static async Task<ResultadoConciliacionLiquidacionMp> MarcarPendienteAsync(
        ISesionPersistencia sesion,
        LiquidacionMpRegistrada liquidacion,
        string observacion,
        CancellationToken cancellationToken)
    {
        // Ya está pendiente por defecto; se guarda la observación sin cambiar
        // de estado para conciliación asistida.
        return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, EstadosConciliacionCobroManualMp.Pendiente, null, null, null, null, null, observacion);
    }

    private async Task<ResultadoConciliacionLiquidacionMp> MarcarRevisionAsync(
        ISesionPersistencia sesion,
        LiquidacionMpRegistrada liquidacion,
        string observacion,
        CancellationToken cancellationToken)
    {
        await _liquidaciones.ActualizarConciliacionLiquidacionAsync(sesion, liquidacion.Id, EstadosConciliacionCobroManualMp.RequiereIntervencion, null, null, null, null, observacion, cancellationToken);
        return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, EstadosConciliacionCobroManualMp.RequiereIntervencion, null, null, null, null, null, observacion);
    }

    /// <summary>Lista de candidatos para conciliación asistida (no vincula nada).</summary>
    public async Task<IReadOnlyList<CandidatoConciliacionMp>> SugerirAsync(
        string liquidacionId,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);

        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var liquidacion = await _liquidaciones.ObtenerLiquidacionAsync(lectura, liquidacionId, cancellationToken)
            ?? throw new InvalidOperationException("La liquidación no existe.");

        var candidatos = new List<CandidatoConciliacionMp>();
        if (liquidacion.ExternalReference is { Length: > 3 } referencia &&
            referencia.StartsWith("LI-", StringComparison.Ordinal))
        {
            var operacion = await _mp.ObtenerOperacionAsync(lectura, referencia[3..], cancellationToken);
            if (operacion?.VentaId is not null)
            {
                var venta = await _ventas.ObtenerPorIdAsync(lectura, operacion.VentaId, cancellationToken);
                if (venta is not null)
                    candidatos.Add(new CandidatoConciliacionMp("integrado", venta.Id, venta.Numero, await BuscarPagoMercadoPagoAsync(lectura, venta.Id, cancellationToken), null, operacion.Id, venta.TotalMinor, venta.ConfirmadaEn ?? venta.CreadaEn, "Coincide EXTERNAL_REFERENCE de la saga integrada."));
            }
        }

        var declarado = ImporteDeclaradoMinor(liquidacion);
        if (declarado is { } importe)
        {
            var manuales = await _mp.ListarCobrosManualesSimilaresAsync(lectura, importe, null, "0001-01-01T00:00:00.0000000Z", cancellationToken);
            foreach (var manual in manuales)
                candidatos.Add(new CandidatoConciliacionMp("manual", manual.VentaId, null, manual.PagoId, manual.Id, null, manual.ImporteMinor, manual.DeclaradoEn, "Cobro manual declarado con el mismo importe."));
        }

        return candidatos;
    }

    /// <summary>
    /// Conciliación asistida: la Dueña confirma que la fila corresponde a una
    /// venta concreta. Queda registrada como vínculo humano.
    /// </summary>
    public async Task<ResultadoConciliacionLiquidacionMp> ConfirmarAsync(
        string liquidacionId,
        string ventaId,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);
        if (string.IsNullOrWhiteSpace(ventaId)) throw new InvalidOperationException("Elegí la venta a la que corresponde el movimiento.");

        var creadoEn = _reloj.UtcNow().ToString("o");
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var liquidacion = await _liquidaciones.ObtenerLiquidacionAsync(sesion, liquidacionId, cancellationToken)
            ?? throw new InvalidOperationException("La liquidación no existe.");
        if (await _liquidaciones.ExisteConciliacionDeLiquidacionAsync(sesion, liquidacionId, cancellationToken))
            throw new InvalidOperationException("La liquidación ya fue conciliada.");

        var venta = await _ventas.ObtenerPorIdAsync(sesion, ventaId, cancellationToken)
            ?? throw new InvalidOperationException("La venta no existe.");

        var manual = await _mp.ObtenerCobroManualPorVentaAsync(sesion, venta.Id, cancellationToken);
        string? operacionMpId = null;
        string? pagoId;
        if (manual is not null)
        {
            pagoId = manual.PagoId;
            await _mp.ActualizarEstadoConciliacionCobroManualAsync(sesion, manual.Id, EstadosConciliacionCobroManualMp.Conciliada, creadoEn, cancellationToken);
        }
        else
        {
            var operacion = await _mp.ObtenerOperacionPorVentaAsync(sesion, venta.Id, cancellationToken);
            operacionMpId = operacion?.Id;
            pagoId = await BuscarPagoMercadoPagoAsync(sesion, venta.Id, cancellationToken);
        }

        var (estado, detalle) = Comparar(ImporteDeclaradoMinor(liquidacion), venta.TotalMinor);
        await _liquidaciones.ActualizarConciliacionLiquidacionAsync(sesion, liquidacion.Id, estado, venta.Id, pagoId, operacionMpId, manual?.Id, detalle, cancellationToken);
        await _liquidaciones.InsertarConciliacionAsync(sesion, liquidacion.Id, venta.Id, pagoId, operacionMpId, manual?.Id, TiposMatchConciliacionMp.AsistidoHumano, detalle, actor.Id, creadoEn, cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return new ResultadoConciliacionLiquidacionMp(liquidacion.Id, estado, TiposMatchConciliacionMp.AsistidoHumano, venta.Id, pagoId, operacionMpId, manual?.Id, detalle);
    }

    private async Task<string?> BuscarPagoMercadoPagoAsync(ISesionPersistencia sesion, string ventaId, CancellationToken cancellationToken)
    {
        var pagos = await _pagos.ListarPorVentaAsync(sesion, ventaId, cancellationToken);
        return pagos.FirstOrDefault(p => p.Medio == MediosPago.MercadoPago)?.Id;
    }

    private static long? ImporteDeclaradoMinor(LiquidacionMpRegistrada liquidacion) =>
        liquidacion.GrossMinor ?? liquidacion.NetCreditoMinor;

    private static (string Estado, string? Detalle) Comparar(long? declarado, long esperado)
    {
        if (declarado is null)
            return (EstadosConciliacionCobroManualMp.ConDiferencia, "El reporte no informa el importe bruto: revisar.");
        if (declarado.Value == esperado)
            return (EstadosConciliacionCobroManualMp.Conciliada, null);
        var diferencia = declarado.Value - esperado;
        return (EstadosConciliacionCobroManualMp.ConDiferencia,
            $"El importe del reporte ({declarado.Value}) no coincide con la venta ({esperado}). Diferencia: {diferencia} centavos.");
    }
}
