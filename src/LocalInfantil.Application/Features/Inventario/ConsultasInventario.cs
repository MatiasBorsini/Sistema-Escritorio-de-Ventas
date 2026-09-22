using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Consultas de existencias e historial. Requieren sesión vigente pero ningún
/// permiso específico (interino hasta D-03). Las existencias se derivan de
/// movimientos confirmados; el historial conserva variantes inactivas. Sin
/// clasificación de dañado/no vendible: definición pendiente, no inventada.
/// </summary>
public sealed class ConsultasInventario
{
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioConteos _conteos;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasInventario(
        IRepositorioMovimientos movimientos,
        IRepositorioVariantes variantes,
        IRepositorioConteos conteos,
        GestorSesiones sesiones,
        IFabricaSesionesPersistencia fabrica)
    {
        _movimientos = movimientos;
        _variantes = variantes;
        _conteos = conteos;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<ExistenciaVariante?> ObtenerExistenciaAsync(string sku, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorSkuAsync(sesion, sku, cancellationToken);
        if (variante is null)
        {
            return null;
        }

        var existencias = await _movimientos.SumarPorVarianteAsync(sesion, variante.Id, cancellationToken);
        return new ExistenciaVariante(variante.Id, variante.Sku, existencias);
    }

    public async Task<IReadOnlyList<MovimientoHistorial>> HistorialPorSkuAsync(string sku, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        var variante = await _variantes.ObtenerPorSkuAsync(sesion, sku, cancellationToken);
        if (variante is null)
        {
            return Array.Empty<MovimientoHistorial>();
        }

        return await _movimientos.ListarPorVarianteAsync(sesion, variante.Id, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<InformeConteoLinea>> InformeConteoAsync(string conteoId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _conteos.InformeAsync(sesion, conteoId, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null)
        {
            throw new SesionInvalidaException();
        }

        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
