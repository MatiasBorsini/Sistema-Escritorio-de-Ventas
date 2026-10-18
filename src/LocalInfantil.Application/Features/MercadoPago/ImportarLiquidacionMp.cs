using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Aplica (importa) un reporte de liquidación de Mercado Pago.
///
/// - Idempotente por hash de archivo: reimportar el mismo reporte no duplica.
/// - Identidad de fila = (importación, ordinal): dos filas idénticas del mismo
///   archivo se conservan por separado; nunca se descarta una fila en silencio.
/// - Deduplicación entre reportes solapados SOLO con identidad remota confiable
///   (identificador de operación del proveedor). Sin identidad suficiente, una
///   fila con contenido repetido se conserva pero queda en revisión: no se
///   concilia automáticamente ni se cuenta dos veces.
/// - Registra la importación y sus filas en una única transacción.
/// - Intenta conciliación automática SOLO por identidad remota inequívoca.
/// - Un archivo parcialmente inválido se registra como 'parcial'/'rechazado'
///   y nunca se presenta como conciliación exitosa.
/// </summary>
public sealed class ImportarLiquidacionMp
{
    private readonly IRepositorioLiquidacionesMp _liquidaciones;
    private readonly ConciliarLiquidacionMp _conciliar;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IReloj _reloj;

    public ImportarLiquidacionMp(
        IRepositorioLiquidacionesMp liquidaciones,
        ConciliarLiquidacionMp conciliar,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IReloj reloj)
    {
        _liquidaciones = liquidaciones;
        _conciliar = conciliar;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _reloj = reloj;
    }

    public async Task<ResultadoImportacionMp> EjecutarAsync(
        byte[] contenido,
        string nombreArchivo,
        string? cuentaMp,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);

        var hash = LectorReporteLiquidacionMp.HashArchivo(contenido);
        var texto = LectorReporteLiquidacionMp.Decodificar(contenido);
        var parseo = LectorReporteLiquidacionMp.Parsear(texto, nombreArchivo);
        if (parseo.Filas.Count == 0)
            throw new FormatoReporteInvalidoMpException(
                "El reporte no contiene movimientos válidos. No se importó nada.");

        var creadoEn = _reloj.UtcNow().ToString("o");
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);

        if (await _liquidaciones.ExisteImportacionPorHashAsync(sesion, hash, cancellationToken))
            throw new InvalidOperationException("Este archivo ya fue importado. No se importó nada.");

        var importacionId = await _liquidaciones.InsertarImportacionAsync(sesion, new ImportacionMpRegistrada
        {
            Origen = "archivo",
            ArchivoNombre = nombreArchivo,
            ArchivoHash = hash,
            CuentaMp = cuentaMp,
            FormatoReporte = parseo.FormatoReporte,
            UsuarioId = actor.Id,
            CreadoEn = creadoEn,
            Estado = EstadosImportacionMp.Validado
        }, cancellationToken);

        var aplicadas = 0;
        var duplicadas = 0;
        var conciliadasAuto = 0;
        var pendientes = 0;
        var enRevision = 0;

        // Snapshot de lo ya importado ANTES de insertar nada de este archivo:
        // distingue un duplicado entre reportes de una fila idéntica repetida
        // dentro del mismo archivo, sin descartar filas legítimas.
        var identidades = parseo.Filas
            .Select(f => f.IdentidadRemota)
            .Where(i => i is not null)
            .Select(i => i!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var identidadesExistentes = new HashSet<string>(
            await _liquidaciones.ListarIdentidadesRemotasExistentesAsync(sesion, identidades, cancellationToken),
            StringComparer.Ordinal);
        var hashes = parseo.Filas.Select(f => f.FilaHash).Distinct(StringComparer.Ordinal).ToArray();
        var hashesExistentes = new HashSet<string>(
            await _liquidaciones.ListarHashesExistentesAsync(sesion, hashes, cancellationToken),
            StringComparer.Ordinal);

        foreach (var fila in parseo.Filas)
        {
            // Duplicado entre reportes solapados: SOLO con identidad remota
            // confiable. Nunca por importe, fecha o descripción.
            if (fila.IdentidadRemota is { } identidad && identidadesExistentes.Contains(identidad))
            {
                duplicadas++;
                continue;
            }

            var id = await _liquidaciones.InsertarLiquidacionAsync(sesion, importacionId, fila, creadoEn, cancellationToken);
            aplicadas++;
            var liquidacion = await _liquidaciones.ObtenerLiquidacionAsync(sesion, id, cancellationToken)
                ?? throw new InvalidOperationException("No se pudo releer la liquidación recién insertada.");

            // Sin identidad remota y con contenido idéntico a un movimiento de
            // otro reporte: posible duplicado ambiguo. Se conserva, pero queda
            // en revisión: sin conciliación automática ni doble conteo.
            if (fila.IdentidadRemota is null && hashesExistentes.Contains(fila.FilaHash))
            {
                await _liquidaciones.ActualizarConciliacionLiquidacionAsync(
                    sesion, id, EstadosConciliacionCobroManualMp.RequiereIntervencion, null, null, null, null,
                    "Posible duplicado: el contenido coincide con un movimiento de otro reporte y no hay identidad remota confiable. Revisar antes de conciliar.",
                    cancellationToken);
                enRevision++;
                continue;
            }

            var resultado = await _conciliar.IntentarAutomaticaAsync(sesion, liquidacion, creadoEn, cancellationToken);
            if (resultado is null) continue;
            if (resultado.VentaId is not null) conciliadasAuto++;
            else if (resultado.EstadoConciliacion is EstadosConciliacionCobroManualMp.Pendiente
                     or EstadosConciliacionCobroManualMp.RequiereIntervencion
                     or EstadosConciliacionCobroManualMp.ConDiferencia) pendientes++;
        }

        var estado = parseo.Errores.Count == 0
            ? EstadosImportacionMp.Aplicado
            : aplicadas > 0 ? EstadosImportacionMp.Parcial : EstadosImportacionMp.Rechazado;
        var totales = parseo.Filas.Count + parseo.Errores.Count;
        await _liquidaciones.ActualizarResultadoImportacionAsync(sesion, importacionId, estado, totales, aplicadas, parseo.Errores.Count, cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);

        return new ResultadoImportacionMp(importacionId, estado, totales, aplicadas, duplicadas, parseo.Errores.Count, conciliadasAuto, pendientes, enRevision);
    }
}
