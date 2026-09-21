using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Fija un precio cerrando el vigente anterior y creando el nuevo en una
/// única transacción con auditoría. El historial se conserva; la unicidad de
/// vigencia la garantiza la base (índice V4). Solo centavos ARS >= 0.
/// </summary>
public sealed class FijarPrecio
{
    private readonly IRepositorioPrecios _precios;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public FijarPrecio(
        IRepositorioPrecios precios,
        IRepositorioVariantes variantes,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _precios = precios;
        _variantes = variantes;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(string varianteId, long importeMinor, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (importeMinor < 0)
        {
            throw new ArgumentException("El importe no puede ser negativo.", nameof(importeMinor));
        }

        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.PreciosGestionar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        await _precios.FinalizarVigenteAsync(sesion, varianteId, fecha, cancellationToken);
        var id = await _precios.FijarAsync(
            sesion,
            new NuevoPrecio(varianteId, importeMinor, "ARS", fecha, null),
            fecha,
            cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.precio_fijado", ActorAuditoria.Usuario, actor.Id, "variante", varianteId, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
