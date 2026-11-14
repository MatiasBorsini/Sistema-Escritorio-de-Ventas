using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Previsualiza un reporte de liquidación (dry-run): valida el formato,
/// cuenta filas válidas/reconocidas/desconocidas/duplicadas y errores, y
/// avisa si el archivo ya fue importado. NO aplica nada.
/// </summary>
public sealed class PrevisualizarImportacionLiquidacionMp
{
    private readonly IRepositorioLiquidacionesMp _liquidaciones;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public PrevisualizarImportacionLiquidacionMp(
        IRepositorioLiquidacionesMp liquidaciones,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica)
    {
        _liquidaciones = liquidaciones;
        _autorizador = autorizador;
        _fabrica = fabrica;
    }

    public async Task<PrevisualizacionImportacionMp> EjecutarAsync(
        byte[] contenido,
        string nombreArchivo,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contenido);
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Conciliar, cancellationToken);

        var hash = LectorReporteLiquidacionMp.HashArchivo(contenido);
        var texto = LectorReporteLiquidacionMp.Decodificar(contenido);
        var parseo = LectorReporteLiquidacionMp.Parsear(texto, nombreArchivo);

        await using var lectura = await _fabrica.IniciarLecturaAsync(cancellationToken);
        var yaImportado = await _liquidaciones.ExisteImportacionPorHashAsync(lectura, hash, cancellationToken);

        var hashes = parseo.Filas.Select(f => f.FilaHash).Distinct(StringComparer.Ordinal).ToArray();
        var existentes = await _liquidaciones.ListarHashesExistentesAsync(lectura, hashes, cancellationToken);
        var set = new HashSet<string>(existentes, StringComparer.Ordinal);
        var duplicadas = parseo.Filas.Count(f => set.Contains(f.FilaHash));

        return new PrevisualizacionImportacionMp(
            nombreArchivo,
            hash,
            parseo.FormatoReporte,
            parseo.Columnas,
            parseo.Filas.Count + parseo.Errores.Count,
            parseo.Filas.Count,
            parseo.FilasReconocidas,
            parseo.FilasDesconocidas,
            duplicadas,
            parseo.Errores.Count,
            yaImportado,
            parseo.Errores);
    }
}
