using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Edición de talle/color (reemplazo total). Código interno y producto inmutables. Mantiene la regla sin duplicados.</summary>
public sealed class EditarVariante
{
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public EditarVariante(
        IRepositorioVariantes variantes,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids)
    {
        _variantes = variantes;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
    }

    public async Task EjecutarAsync(string id, EdicionVariante edicion, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edicion);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.VariantesGestionar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var existente = await _variantes.ObtenerPorIdAsync(sesion, id, cancellationToken);
        if (existente is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var hermanas = await _variantes.ListarPorProductoAsync(sesion, existente.ProductoId, cancellationToken);
        if (hermanas.Any(v => v.Id != id && NormalizacionVariante.MismaCombinacion(v.Talle, v.Color, edicion.Talle, edicion.Color)))
        {
            throw new InvalidOperationException("Ya existe una variante con ese talle y color.");
        }

        await _variantes.ActualizarAsync(sesion, id, edicion, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.variante_edicion", ActorAuditoria.Usuario, actor.Id, "variante", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
