using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Alta de producto con código interno automático jerárquico {PREFIJO}-{NNN}
/// (ej REM-001). La categoría es obligatoria y debe ser una categoría activa
/// válida: no se asigna "Otros" automáticamente ("Otros" es una elección
/// explícita). Secuencia por categoría dentro de la misma sesión
/// BEGIN IMMEDIATE del alta (sin transacciones anidadas). Auditoría en la
/// misma transacción.
/// </summary>
public sealed class CrearProducto
{
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioCategorias _categorias;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearProducto(
        IRepositorioProductos productos,
        IRepositorioCategorias categorias,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _productos = productos;
        _categorias = categorias;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(NuevoProducto datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.ProductosGestionar, cancellationToken);

        if (string.IsNullOrWhiteSpace(datos.Nombre))
        {
            throw new ArgumentException("El nombre es obligatorio.", nameof(datos));
        }

        if (string.IsNullOrWhiteSpace(datos.CategoriaId))
        {
            throw new InvalidOperationException("Seleccioná una categoría.");
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);

        var categoria = await _categorias.ObtenerPorIdAsync(sesion, datos.CategoriaId, cancellationToken);
        if (categoria is null || !categoria.Activa)
        {
            throw new InvalidOperationException("Seleccioná una categoría.");
        }

        var numero = await _productos.ObtenerSiguienteNumeroProductoAsync(sesion, categoria.Id, cancellationToken);
        var codigoInterno = $"{categoria.Prefijo}-{numero:D3}";

        var id = await _productos.InsertarAsync(sesion, datos, codigoInterno, _reloj.UtcNow().ToString("o"), cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.producto_alta", ActorAuditoria.Usuario, actor.Id, "producto", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
