using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Cobro manual de Point (modalidad manual): la empleada cobra el importe en
/// la terminal y registra la aprobación observada. Reutiliza la confirmación
/// atómica de venta (precio, stock, caja, auditoría) y añade, en la MISMA
/// transacción, el registro del cobro manual.
///
/// Reglas de seguridad:
/// - Solo cuando la modalidad local resuelta es "manual".
/// - Exactamente un pago de Mercado Pago por el importe declarado.
/// - Sin fiado combinado.
/// - Si hay un cobro integrado pendiente de resolver en la sesión de caja,
///   se bloquea (no se admite un segundo cobro incierto).
/// - Una referencia de comprobante no puede quedar vinculada a dos cobros.
/// - Nunca crea una operación de saga ni inventa payment_id: el cobro queda
///   pendiente de conciliación y no se presenta como verificado por MP.
/// </summary>
public sealed class RegistrarCobroManualPoint
{
    /// <summary>Ventana (minutos) para advertir posibles duplicados manuales.</summary>
    public const int VentanaDuplicadoMinutos = 15;

    private readonly IRepositorioMercadoPago _mp;
    private readonly IAlmacenConfigMercadoPago _config;
    private readonly ConfirmarVenta _confirmar;
    private readonly IRepositorioCaja _caja;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public RegistrarCobroManualPoint(
        IRepositorioMercadoPago mp,
        IAlmacenConfigMercadoPago config,
        ConfirmarVenta confirmar,
        IRepositorioCaja caja,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj)
    {
        _mp = mp;
        _config = config;
        _confirmar = confirmar;
        _caja = caja;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _reloj = reloj;
    }

    public async Task<ResultadoConfirmacionVenta> EjecutarAsync(
        IReadOnlyList<NuevaLineaVenta> lineas,
        IReadOnlyList<NuevoPago> pagos,
        string operacionId,
        string? comprobanteRef,
        string? idSesion,
        bool confirmarPosibleDuplicado = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(pagos);
        if (idSesion is null) throw new SesionInvalidaException();

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosVentas.VentasConfirmar, cancellationToken);

        var config = _config.Leer();
        var modo = ModosPoint.Resolver(config.ModoLocalInfantil, config.TerminalId is not null);
        if (modo != ModosPoint.Manual)
            throw new InvalidOperationException("El cobro manual de Point no está habilitado en esta instalación.");

        var filasMp = pagos.Where(p => p.Medio == MediosPago.MercadoPago).ToArray();
        if (filasMp.Length != 1)
            throw new InvalidOperationException("El cobro manual requiere exactamente un pago de Mercado Pago.");
        var importeMp = filasMp[0].ImporteMinor;
        if (importeMp <= 0)
            throw new InvalidOperationException("El importe del cobro Point no es válido.");

        var comprobante = string.IsNullOrWhiteSpace(comprobanteRef) ? null : comprobanteRef.Trim();
        if (comprobante is not null && comprobante.Length > 64)
            throw new InvalidOperationException("La referencia del comprobante es demasiado larga (máximo 64 caracteres).");

        // Pre-chequeo en lectura: caja abierta, sin cobros inciertos pendientes
        // y sin confirmación previa. La confirmación revalida la unicidad.
        var ahora = _reloj.UtcNow();
        var declaradoEn = ahora.ToString("o");
        var desdeVentana = ahora.AddMinutes(-VentanaDuplicadoMinutos).ToString("o");
        string cajaSesionId;
        await using (var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken))
        {
            var cajaAbierta = await _caja.ObtenerAbiertaAsync(lectura, cancellationToken)
                ?? throw new InvalidOperationException("No hay una caja abierta. Abrí la caja antes de registrar una venta.");
            cajaSesionId = cajaAbierta.Id;

            var pendientes = await _mp.ListarPendientesPorSesionAsync(lectura, cajaSesionId, cancellationToken);
            if (pendientes.Count > 0)
                throw new InvalidOperationException("Hay un cobro de Mercado Pago pendiente de resolver. Resolvelo antes de registrar un cobro manual.");

            if (await _mp.ExisteCobroManualPorOperacionAsync(lectura, operacionId, cancellationToken))
                throw new InvalidOperationException("Esta venta ya fue confirmada.");

            // Un comprobante identifica un único cobro físico: no puede quedar
            // vinculado a dos operaciones. Se bloquea incluso con confirmación,
            // porque es una colisión determinista, no una mera coincidencia.
            if (comprobante is not null && await _mp.ExisteCobroManualPorComprobanteAsync(lectura, comprobante, cancellationToken))
                throw new InvalidOperationException("La referencia de comprobante ya está registrada en otro cobro de Point. Verificá el ticket.");

            // Dedup manual (§5.4): mismo importe (y terminal) en la ventana
            // reciente. Nunca auto-descarta ni auto-vincula: exige confirmación.
            if (!confirmarPosibleDuplicado)
            {
                var similares = await _mp.ListarCobrosManualesSimilaresAsync(lectura, importeMp, config.TerminalId, desdeVentana, cancellationToken);
                if (similares.Count > 0) throw new CobroManualDuplicadoSospechosoException(similares);
            }
        }

        return await _confirmar.EjecutarConRegistroPagoMercadoPagoAsync(
            lineas,
            pagos,
            operacionId,
            idSesion,
            clienteId: null,
            vencimiento: null,
            usarCuentaCorriente: false,
            alInsertarPagoMercadoPago: (sesion, ventaId, pagoId, ct) =>
                _mp.InsertarCobroManualAsync(
                    sesion,
                    operacionId,
                    ventaId,
                    pagoId,
                    importeMp,
                    comprobante,
                    config.TerminalId,
                    config.PosId,
                    declaradoEn,
                    EstadosConciliacionCobroManualMp.Pendiente,
                    actor.Id,
                    cajaSesionId,
                    ct),
            cancellationToken: cancellationToken);
    }
}
