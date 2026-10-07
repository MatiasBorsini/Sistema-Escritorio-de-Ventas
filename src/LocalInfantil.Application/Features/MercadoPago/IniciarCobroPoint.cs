using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Inicia un cobro Point: valida (precios, disponible, caja, permisos),
/// persiste saga + snapshot durable ANTES de llamar a Mercado Pago, y crea
/// la order con idempotency key propia. Timeout ≠ rechazo: queda en
/// requiere_conciliacion con "Verificando el estado del cobro...".
/// </summary>
public sealed class IniciarCobroPoint
{
    private readonly IRepositorioMercadoPago _mp;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioProductos _productos;
    private readonly IRepositorioPrecios _precios;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioCaja _caja;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IAlmacenConfigMercadoPago _config;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public IniciarCobroPoint(
        IRepositorioMercadoPago mp,
        IRepositorioVariantes variantes,
        IRepositorioProductos productos,
        IRepositorioPrecios precios,
        IRepositorioMovimientos movimientos,
        IRepositorioApartados apartados,
        IRepositorioCaja caja,
        IMercadoPagoCredencialProvider credenciales,
        IAlmacenConfigMercadoPago config,
        IMercadoPagoPointClient cliente,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _mp = mp;
        _variantes = variantes;
        _productos = productos;
        _precios = precios;
        _movimientos = movimientos;
        _apartados = apartados;
        _caja = caja;
        _credenciales = credenciales;
        _config = config;
        _cliente = cliente;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(
        IReadOnlyList<NuevaLineaVenta> lineas,
        IReadOnlyList<NuevoPago> pagos,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(pagos);
        if (idSesion is null) throw new SesionInvalidaException();
        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosVentas.VentasConfirmar, cancellationToken);

        if (lineas.Count == 0) throw new ArgumentException("La venta no tiene líneas.", nameof(lineas));
        if (!pagos.Any(p => p.Medio == MediosPago.MercadoPago))
            throw new ArgumentException("El cobro Point requiere el medio Mercado Pago.", nameof(pagos));
        if (pagos.Count(p => p.Medio == MediosPago.MercadoPago) > 1)
            throw new ArgumentException("Una sola fila de Mercado Pago por venta.", nameof(pagos));
        foreach (var pago in pagos)
        {
            if (!MediosPago.Todos.Contains(pago.Medio))
                throw new ArgumentException($"Medio de pago no admitido: '{pago.Medio}'.", nameof(pagos));
            if (pago.ImporteMinor <= 0)
                throw new ArgumentException($"Importe inválido para el medio '{pago.Medio}'.", nameof(pagos));
            if (pago.Medio == MediosPago.Efectivo)
            {
                if (pago.RecibidoMinor is null || pago.RecibidoMinor < pago.ImporteMinor)
                    throw new ArgumentException("El efectivo recibido es menor al importe correspondiente.", nameof(pagos));
            }
            else if (pago.RecibidoMinor is not null)
                throw new ArgumentException($"El medio '{pago.Medio}' no lleva recibido.", nameof(pagos));
        }

        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");
        var terminalConfigurada = _config.Leer().TerminalId;
        if (string.IsNullOrWhiteSpace(terminalConfigurada))
            throw new InvalidOperationException("No hay terminal seleccionada. Configurala antes de cobrar.");

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var cajaAbierta = await _caja.ObtenerAbiertaAsync(sesion, cancellationToken)
            ?? throw new InvalidOperationException("No hay una caja abierta. Abrí la caja antes de cobrar.");

        var pendientes = await _mp.ListarPendientesPorSesionAsync(sesion, cajaAbierta.Id, cancellationToken);
        if (pendientes.Count > 0)
            throw new InvalidOperationException("Hay un cobro de Mercado Pago pendiente de resolver.");

        var fecha = _reloj.UtcNow().ToString("o");
        var snapshot = new List<(string VarianteId, string CodigoInterno, string Nombre, string? Talle, string? Color, long Cantidad, long Precio)>();
        foreach (var linea in lineas)
        {
            if (linea.Cantidad <= 0) throw new ArgumentException("Cantidad inválida.", nameof(lineas));
            var variante = await _variantes.ObtenerPorIdAsync(sesion, linea.VarianteId, cancellationToken);
            if (variante is null || !variante.Activa)
                throw new InvalidOperationException("Hay variantes sin stock disponible en esta venta.");
            var disponible = await _movimientos.SumarPorVarianteAsync(sesion, linea.VarianteId, cancellationToken)
                - await _apartados.SumarApartadoPorVarianteAsync(sesion, linea.VarianteId, cancellationToken);
            if (disponible < linea.Cantidad)
                throw new InvalidOperationException("No hay unidades disponibles de esta variante.");
            var precio = await _precios.ObtenerVigenteAsync(sesion, linea.VarianteId, fecha, cancellationToken)
                ?? throw new InvalidOperationException("Esta variante no tiene un precio vigente.");
            if (precio.ImporteMinor != linea.PrecioEsperadoMinor)
                throw new InvalidOperationException("El precio cambió. Revisá la venta antes de cobrar.");
            var producto = await _productos.ObtenerPorIdAsync(sesion, variante.ProductoId, cancellationToken);
            snapshot.Add((linea.VarianteId, variante.CodigoInterno, producto?.Nombre ?? variante.CodigoInterno, variante.Talle, variante.Color, linea.Cantidad, precio.ImporteMinor));
        }

        var total = snapshot.Sum(l => l.Cantidad * l.Precio);
        if (pagos.Sum(p => p.ImporteMinor) != total)
            throw new InvalidOperationException("Los medios deben sumar exactamente el total (sin fiado combinado con Point).");

        var importeMp = pagos.Single(p => p.Medio == MediosPago.MercadoPago).ImporteMinor;

        TerminalPoint terminal;
        try
        {
            terminal = await VerificarTerminalAsync(token, terminalConfigurada, cancellationToken);
        }
        catch (ExcepcionMercadoPago ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }

        if (terminal.ModoOperativo != "PDV")
            throw new InvalidOperationException("La terminal no está en modo integrado. Activalo antes de cobrar.");

        var operacionId = _ids.NuevoId();
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var externalReference = $"LI-{operacionId}";

        var mpId = await _mp.InsertarOperacionAsync(sesion, operacionId, idempotencyKey, externalReference, terminal.Id, importeMp, cajaAbierta.Id, actor.Id, fecha, cancellationToken);
        foreach (var l in snapshot)
            await _mp.InsertarLineaAsync(sesion, mpId, l.VarianteId, l.CodigoInterno, l.Nombre, l.Talle, l.Color, l.Cantidad, l.Precio, cancellationToken);
        foreach (var p in pagos)
            await _mp.InsertarPagoPlanificadoAsync(sesion, mpId, p.Medio, p.ImporteMinor, p.RecibidoMinor, cancellationToken);

        await _mp.ActualizarEstadoAsync(sesion, mpId, EstadosLocalesMp.CreandoOrder, null, null, null, null, _reloj.UtcNow().ToString("o"), cancellationToken);

        OrdenMp orden;
        try
        {
            orden = await _cliente.CrearOrdenAsync(token, idempotencyKey, externalReference, importeMp, terminal.Id, $"Venta local {operacionId[..8]}", cancellationToken);
        }
        catch (ExcepcionMercadoPago ex) when (ex.Codigo == "already_queued_order_for_terminal")
        {
            await _mp.ActualizarEstadoAsync(sesion, mpId, EstadosLocalesMp.RequiereConciliacion, null, null, null,
                "La terminal ya tiene un cobro pendiente.", _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException("La terminal ya tiene un cobro pendiente. Revisá antes de reintentar.");
        }
        catch (ExcepcionMercadoPago ex)
        {
            await _mp.ActualizarEstadoAsync(sesion, mpId, EstadosLocalesMp.Fallida, null, null, null,
                ex.Message, _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            await _mp.ActualizarEstadoAsync(sesion, mpId, EstadosLocalesMp.RequiereConciliacion, null, null, null,
                "Sin conexión al crear el cobro.", _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await _mp.ActualizarEstadoAsync(sesion, mpId, EstadosLocalesMp.RequiereConciliacion, null, null, null,
                "Timeout al crear el cobro.", _reloj.UtcNow().ToString("o"), cancellationToken);
            await sesion.ConfirmarAsync(cancellationToken);
            throw new InvalidOperationException("Verificando el estado del cobro...");
        }

        var siguiente = orden.Estado == EstadosOrdenMp.Processed
            ? EstadosLocalesMp.ProcesadaRemota
            : EstadosLocalesMp.EsperandoTerminal;
        await _mp.ActualizarEstadoAsync(sesion, mpId, siguiente, orden.Estado, orden.Id, null, null, _reloj.UtcNow().ToString("o"), cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return operacionId;
    }

    private async Task<TerminalPoint> VerificarTerminalAsync(string token, string terminalId, CancellationToken cancellationToken)
    {
        int desplazamiento = 0;
        while (true)
        {
            var pagina = await _cliente.ListarTerminalesAsync(token, 50, desplazamiento, cancellationToken);
            var terminal = pagina.Terminales.FirstOrDefault(t => t.Id == terminalId);
            if (terminal is not null) return terminal;
            desplazamiento += pagina.Terminales.Count;
            if (desplazamiento >= pagina.Total || pagina.Terminales.Count == 0)
                throw new InvalidOperationException("La terminal configurada ya no aparece en Mercado Pago. Seleccioná una terminal disponible.");
        }
    }
}
