using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Corrige cantidades de un borrador antes de aprobarlo, con auditoría de la
/// corrección. Solo borradores: lo aprobado no se toca.
/// </summary>
public sealed class CorregirConteo
{
    private readonly IRepositorioConteos _conteos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public CorregirConteo(
        IRepositorioConteos conteos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _conteos = conteos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string conteoId, IReadOnlyList<FilaConteo> filas, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filas);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.ConteoRegistrar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var conteo = await _conteos.ObtenerAsync(sesion, conteoId, cancellationToken);
        if (conteo is null)
        {
            throw new InvalidOperationException("El conteo no existe.");
        }

        if (conteo.Estado != "borrador")
        {
            throw new InvalidOperationException("Solo un borrador admite correcciones.");
        }

        var conocidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detalle in await _conteos.ListarDetallesAsync(sesion, conteoId, cancellationToken))
        {
            conocidos.Add(detalle.VarianteId);
        }

        foreach (var fila in filas)
        {
            if (fila.CantidadContada < 0)
            {
                throw new ArgumentException($"Cantidad inválida para la variante '{fila.VarianteId}'.", nameof(filas));
            }

            if (!conocidos.Contains(fila.VarianteId))
            {
                throw new InvalidOperationException($"La variante '{fila.VarianteId}' no pertenece al conteo.");
            }

            await _conteos.ActualizarDetalleAsync(sesion, conteoId, fila.VarianteId, fila.CantidadContada, cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "inventario.conteo_corregido", ActorAuditoria.Usuario, actor.Id, null, null, $"{filas.Count} filas"),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
