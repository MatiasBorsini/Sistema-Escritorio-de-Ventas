using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Alta de variante con código interno automático jerárquico
/// {CODIGO_PRODUCTO}-{NN} (ej REM-001-01). La usuaria no escribe el código:
/// se genera como autoridad dentro de la misma sesión BEGIN IMMEDIATE del
/// alta. Regla: sin duplicar Talle+Color dentro del producto (trim,
/// case-insensitive, NULL ≡ vacío). Producto inmutable desde el alta.
/// Auditoría en la misma transacción.
/// </summary>
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

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var producto = await _productos.ObtenerPorIdAsync(sesion, datos.ProductoId, cancellationToken);
        if (producto is null)
        {
            throw new InvalidOperationException("El producto no existe.");
        }

        if (string.IsNullOrWhiteSpace(producto.CodigoInterno))
        {
            throw new InvalidOperationException("El producto no tiene código interno.");
        }

        var hermanas = await _variantes.ListarPorProductoAsync(sesion, datos.ProductoId, cancellationToken);
        if (hermanas.Any(v => NormalizacionVariante.MismaCombinacion(v.Talle, v.Color, datos.Talle, datos.Color)))
        {
            throw new InvalidOperationException("Ya existe una variante con ese talle y color.");
        }

        // Código jerárquico: misma convención que usa typeahead para buscar.
        var numero = await _variantes.ObtenerSiguienteNumeroVarianteAsync(sesion, datos.ProductoId, cancellationToken);
        var codigoInterno = $"{producto.CodigoInterno}-{numero:D2}";

        var id = await _variantes.InsertarAsync(sesion, datos, codigoInterno, _reloj.UtcNow().ToString("o"), cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.variante_alta", ActorAuditoria.Usuario, actor.Id, "variante", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
