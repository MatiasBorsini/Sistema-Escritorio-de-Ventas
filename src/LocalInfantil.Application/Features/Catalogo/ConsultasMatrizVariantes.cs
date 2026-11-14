using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>Previsualiza un lote sin reservar secuencias ni escribir datos.</summary>
public sealed class ConsultasMatrizVariantes
{
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasMatrizVariantes(IRepositorioProductos productos, IRepositorioVariantes variantes, IAutorizador autorizador, IFabricaSesionesPersistencia fabrica)
        => (_productos, _variantes, _autorizador, _fabrica) = (productos, variantes, autorizador, fabrica);

    public async Task<IReadOnlyList<PropuestaVarianteBatch>> PrevisualizarAsync(string productoId, IReadOnlyList<CombinacionVariante> combinaciones, string? idSesion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(combinaciones);
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosCatalogo.VariantesGestionar, ct);
        ValidarEntrada(combinaciones);

        await using var sesion = await _fabrica.IniciarLecturaAsync(ct);
        var producto = await _productos.ObtenerPorIdAsync(sesion, productoId, ct)
            ?? throw new InvalidOperationException("El producto no existe.");
        if (!producto.Activo) throw new InvalidOperationException("El producto está inactivo.");
        if (string.IsNullOrWhiteSpace(producto.CodigoInterno)) throw new InvalidOperationException("El producto no tiene código interno.");

        var existentes = await _variantes.ListarPorProductoAsync(sesion, productoId, ct);
        var numero = await _variantes.ObtenerUltimoNumeroVarianteAsync(sesion, productoId, ct);
        var siguiente = numero + 1;
        var filas = new List<PropuestaVarianteBatch>(combinaciones.Count);
        foreach (var c in combinaciones)
        {
            var existe = existentes.Any(v => NormalizacionVariante.MismaCombinacion(v.Talle, v.Color, c.Talle, c.Color));
            filas.Add(existe
                ? new PropuestaVarianteBatch(null, c.Talle, c.Color, true)
                : new PropuestaVarianteBatch($"{producto.CodigoInterno}-{siguiente++:D2}", c.Talle, c.Color, false));
        }
        return filas;
    }

    internal static void ValidarEntrada(IReadOnlyList<CombinacionVariante> combinaciones)
    {
        if (combinaciones.Count == 0) throw new InvalidOperationException("Seleccioná al menos una combinación.");
        var vistas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in combinaciones)
        {
            var llave = $"{NormalizacionVariante.NormalizarAtributo(c.Talle)}\0{NormalizacionVariante.NormalizarAtributo(c.Color)}";
            if (!vistas.Add(llave)) throw new InvalidOperationException("La matriz contiene combinaciones repetidas.");
        }
    }
}
