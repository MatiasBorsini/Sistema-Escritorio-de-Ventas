using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Alta de variante con auditoría. SKU y producto inmutables desde el alta.</summary>
public sealed class CrearVariante
{
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioProductos _productos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearVariante(
        IRepositorioVariantes variantes,
        IRepositorioProductos productos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _variantes = variantes;
        _productos = productos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(NuevaVariante datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.VariantesGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(datos.Sku))
        {
            throw new ArgumentException("El SKU es obligatorio.", nameof(datos));
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var producto = await _productos.ObtenerPorIdAsync(sesion, datos.ProductoId, cancellationToken);
        if (producto is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        var id = await _variantes.InsertarAsync(sesion, datos, _reloj.UtcNow().ToString("o"), cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.variante_alta", ActorAuditoria.Usuario, actor.Id, "variante", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
