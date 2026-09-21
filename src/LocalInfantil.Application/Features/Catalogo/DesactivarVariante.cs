using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Desactivación lógica de variante: conserva identidad e historial.</summary>
public sealed class DesactivarVariante
{
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;

    public DesactivarVariante(
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

    public async Task EjecutarAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
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

        await _variantes.DesactivarAsync(sesion, id, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.variante_desactivacion", ActorAuditoria.Usuario, actor.Id, "variante", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
