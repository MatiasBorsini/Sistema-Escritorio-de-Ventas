using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Ventas;

/// <summary>
/// Confirma una venta CONSUMIENDO Disponible (movimientos de egreso, uno por
/// línea) en una sola transacción con auditoría. El operacion_id lo provee el
/// llamador y se acepta una sola vez, incluso tras reaperturas. El precio se
/// revalida dentro de la confirmación: si cambió respecto al carrito, aborta
/// sin cambios y devuelve el detalle (nunca confirma silenciosamente con el
/// precio viejo). Sin descuentos, sin fiado, sin caja (F05).
/// </summary>
public sealed class ConfirmarVenta
{
    private readonly IRepositorioVentas _ventas;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioPrecios _precios;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioPagos _pagos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public ConfirmarVenta(
        IRepositorioVentas ventas,
        IRepositorioVariantes variantes,
        IRepositorioProductos productos,
        IRepositorioPrecios precios,
        IRepositorioMovimientos movimientos,
        IRepositorioApartados apartados,
        IRepositorioPagos pagos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _ventas = ventas;
        _variantes = variantes;
        _productos = productos;
        _precios = precios;
        _movimientos = movimientos;
        _apartados = apartados;
        _pagos = pagos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<ResultadoConfirmacionVenta> EjecutarAsync(
        IReadOnlyList<NuevaLineaVenta> lineas,
        IReadOnlyList<NuevoPago> pagos,
        string operacionId,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(pagos);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosVentas.VentasConfirmar, cancellationToken);

        if (lineas.Count == 0)
        {
            throw new ArgumentException("La venta no tiene líneas.", nameof(lineas));
        }

        if (pagos.Count == 0)
        {
            throw new ArgumentException("La venta no tiene pagos.", nameof(pagos));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var linea in lineas)
        {
            if (linea.Cantidad <= 0)
            {
                throw new ArgumentException($"Cantidad inválida para la variante '{linea.VarianteId}'.", nameof(lineas));
            }

            if (linea.PrecioEsperadoMinor < 0)
            {
                throw new ArgumentException($"Precio inválido para la variante '{linea.VarianteId}'.", nameof(lineas));
            }

            if (!vistos.Add(linea.VarianteId))
            {
                throw new ArgumentException($"Variante duplicada en la venta: '{linea.VarianteId}'.", nameof(lineas));
            }
        }

        foreach (var pago in pagos)
        {
            if (!MediosPago.Todos.Contains(pago.Medio))
            {
                throw new ArgumentException($"Medio de pago no admitido: '{pago.Medio}'.", nameof(pagos));
            }

            if (pago.ImporteMinor <= 0)
            {
                throw new ArgumentException($"Importe inválido para el medio '{pago.Medio}'.", nameof(pagos));
            }

            if (pago.Medio == MediosPago.Efectivo)
            {
                if (pago.RecibidoMinor is null || pago.RecibidoMinor < pago.ImporteMinor)
                {
                    throw new ArgumentException("El efectivo recibido es menor al importe correspondiente.", nameof(pagos));
                }
            }
            else if (pago.RecibidoMinor is not null)
            {
                throw new ArgumentException($"El medio '{pago.Medio}' no lleva recibido.", nameof(pagos));
            }
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _ventas.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta venta ya fue confirmada.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var preciosVigentes = new List<(string VarianteId, string CodigoInterno, string NombreProducto, string? Talle, string? Color, long Cantidad, long PrecioMinor)>();
        var cambios = new List<DetallePrecioCambiado>();
        foreach (var linea in lineas)
        {
            var variante = await _variantes.ObtenerPorIdAsync(sesion, linea.VarianteId, cancellationToken);
            if (variante is null || !variante.Activa)
            {
                throw new InvalidOperationException("Hay variantes sin stock disponible en esta venta.");
            }

            var fisico = await _movimientos.SumarPorVarianteAsync(sesion, linea.VarianteId, cancellationToken);
            var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, linea.VarianteId, cancellationToken);
            var disponible = fisico - apartado;
            if (disponible < linea.Cantidad)
            {
                throw new InvalidOperationException(
                    disponible <= 0
                        ? "No hay unidades disponibles de esta variante."
                        : $"Solo hay {disponible} unidades disponibles de esta variante.");
            }

            var precio = await _precios.ObtenerVigenteAsync(sesion, linea.VarianteId, fecha, cancellationToken);
            if (precio is null)
            {
                throw new InvalidOperationException("Esta variante no tiene un precio vigente.");
            }

            var producto = await _productos.ObtenerPorIdAsync(sesion, variante.ProductoId, cancellationToken);
            var nombre = producto?.Nombre ?? variante.CodigoInterno;
            if (precio.ImporteMinor != linea.PrecioEsperadoMinor)
            {
                cambios.Add(new DetallePrecioCambiado(linea.VarianteId, variante.CodigoInterno, nombre, linea.PrecioEsperadoMinor, precio.ImporteMinor));
            }
            else
            {
                preciosVigentes.Add((linea.VarianteId, variante.CodigoInterno, nombre, variante.Talle, variante.Color, linea.Cantidad, precio.ImporteMinor));
            }
        }

        if (cambios.Count > 0)
        {
            return new ResultadoConfirmacionVenta.PrecioCambiado(cambios);
        }

        var total = preciosVigentes.Sum(p => p.Cantidad * p.PrecioMinor);
        var imputado = pagos.Sum(p => p.ImporteMinor);
        if (imputado < total)
        {
            throw new InvalidOperationException("El importe de los pagos no alcanza el total de la venta.");
        }

        if (imputado > total)
        {
            throw new InvalidOperationException("El importe de los pagos supera el total de la venta.");
        }

        var numero = $"T-{await _ventas.ObtenerSiguienteNumeroAsync(sesion, cancellationToken):D6}";
        var id = await _ventas.InsertarAsync(sesion, numero, operacionId, total, "ARS", actor.Id, fecha, fecha, cancellationToken);
        foreach (var linea in preciosVigentes)
        {
            await _ventas.InsertarLineaAsync(sesion, id, linea.VarianteId, linea.CodigoInterno, linea.NombreProducto, linea.Talle, linea.Color, linea.Cantidad, linea.PrecioMinor, cancellationToken);
            await _movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(linea.VarianteId, "egreso", -linea.Cantidad, "Venta", operacionId, actor.Id),
                fecha,
                cancellationToken);
        }

        foreach (var pago in pagos)
        {
            await _pagos.InsertarAsync(sesion, id, pago.Medio, pago.ImporteMinor, pago.RecibidoMinor, fecha, cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), "venta_confirmada", ActorAuditoria.Usuario, actor.Id, "venta", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return new ResultadoConfirmacionVenta.Confirmada(id, numero, total);
    }
}
