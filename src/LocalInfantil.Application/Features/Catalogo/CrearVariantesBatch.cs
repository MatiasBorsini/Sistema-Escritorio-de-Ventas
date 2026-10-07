using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Alta idempotente y atómica de combinaciones de variantes para un producto existente.</summary>
public sealed class CrearVariantesBatch
{
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioProductos _productos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearVariantesBatch(IRepositorioVariantes variantes, IRepositorioProductos productos, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IGeneradorIds ids, IReloj reloj)
        => (_variantes, _productos, _autorizador, _fabrica, _auditoria, _ids, _reloj) = (variantes, productos, autorizador, fabrica, auditoria, ids, reloj);

    public async Task<ResultadoVariantesBatch> EjecutarAsync(string productoId, IReadOnlyList<CombinacionVariante> combinaciones, string? idSesion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(combinaciones);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.VariantesGestionar, ct);
        ConsultasMatrizVariantes.ValidarEntrada(combinaciones);

        await using var sesion = await _fabrica.IniciarAsync(ct);
        var producto = await _productos.ObtenerPorIdAsync(sesion, productoId, ct)
            ?? throw new InvalidOperationException("El producto no existe.");
        if (!producto.Activo) throw new InvalidOperationException("El producto está inactivo.");
        if (string.IsNullOrWhiteSpace(producto.CodigoInterno)) throw new InvalidOperationException("El producto no tiene código interno.");

        var existentes = await _variantes.ListarPorProductoAsync(sesion, productoId, ct);
        var ids = new List<string>();
        foreach (var c in combinaciones)
        {
            if (existentes.Any(v => NormalizacionVariante.MismaCombinacion(v.Talle, v.Color, c.Talle, c.Color))) continue;

            var numero = await _variantes.ObtenerSiguienteNumeroVarianteAsync(sesion, productoId, ct);
            var datos = new NuevaVariante(productoId, c.Talle, c.Color);
            var id = await _variantes.InsertarAsync(sesion, datos, $"{producto.CodigoInterno}-{numero:D2}", _reloj.UtcNow().ToString("o"), ct);
            await _auditoria.RegistrarAsync(sesion,
                new NuevoEventoAuditoria(_ids.NuevoId(), "catalogo.variante_alta", ActorAuditoria.Usuario, actor.Id, "variante", id, null), ct);
            ids.Add(id);
            existentes = [.. existentes, new VarianteRegistrada(id, productoId, $"{producto.CodigoInterno}-{numero:D2}", c.Talle, c.Color, true)];
        }

        if (ids.Count > 0) await sesion.ConfirmarAsync(ct);
        return new ResultadoVariantesBatch(ids);
    }
}
