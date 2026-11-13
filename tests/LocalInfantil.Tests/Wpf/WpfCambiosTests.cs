using System.Xml.Linq;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfCambiosTests
{
    [Fact]
    public void Vista_separa_operacion_e_historial_y_mantiene_el_reembolso_remoto_explicito()
    {
        var ruta = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "CambiosView.xaml");
        var xaml = File.ReadAllText(ruta);
        var xml = XDocument.Parse(xaml);
        var columnas = xml.Descendants().Single(e => e.Name.LocalName == "Grid"
                && e.Elements().Any(c => c.Name.LocalName == "Grid.ColumnDefinitions"
                    && c.Elements().Any(d => d.Attribute("Width")?.Value == "28*")))
            .Elements().Single(e => e.Name.LocalName == "Grid.ColumnDefinitions")
            .Elements().Where(e => e.Name.LocalName == "ColumnDefinition").ToArray();

        Assert.Equal(new[] { "28*", "72*" }, columnas.Select(c => c.Attribute("Width")?.Value).ToArray());
        Assert.Contains("Tag=\"operacion\"", xaml);
        Assert.Contains("Tag=\"historial\"", xaml);
        Assert.Contains("MouseDoubleClick=\"HistorialListBox_MouseDoubleClick\"", xaml);
        Assert.Contains("Reembolsar en Mercado Pago", xaml);
        Assert.Contains("PuedeContinuarOperacion", xaml);
        Assert.Contains("ErrorCantidadesTexto", xaml);
        Assert.Contains("EstiloGrillaCambios", xaml);
        Assert.Contains("Header=\"Máximo\"", xaml);
        Assert.Contains("Header=\"Cantidad\"", xaml);
        Assert.Contains("Header=\"Apta\"", xaml);
        Assert.Contains("ErrorCantidad", xaml);
        Assert.DoesNotContain("Importe a imputar", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#", xaml);

        var cobrar = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "ConfirmarCambioWindow.xaml"));
        Assert.Contains("ItemsSource=\"{Binding Pagos}\"", cobrar);
        Assert.Contains("Click=\"Confirmar_Click\"", cobrar);
        Assert.Contains("DiferenciaPositiva", cobrar);
        Assert.Contains("DiferenciaNegativa", cobrar);
        Assert.Contains("DiferenciaCero", cobrar);
        Assert.Contains("Medio de reintegro", cobrar);
        Assert.Contains("Motivo del reintegro", cobrar);
        Assert.Contains("El cambio registra el crédito local. El reembolso de Mercado Pago es una operación remota separada.", cobrar);
        Assert.DoesNotContain("Medio de reintegro", xaml);
        Assert.DoesNotContain("ItemsSource=\"{Binding Pagos}\"", xaml);
    }

    [Fact]
    public async Task Cambiar_venta_limpia_su_estado_dependiente_y_no_mezcla_operaciones()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var p1 = await e.PrecioDeAsync(v1);
        var p2 = await e.PrecioDeAsync(v2);
        await VenderAsync(e, v1, p1, "estado-a");
        await VenderAsync(e, v2, p2, "estado-b");
        var ventas = await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion);
        var anterior = Assert.Single(ventas, x => x.Numero == "T-000001");
        var nueva = Assert.Single(ventas, x => x.Numero == "T-000002");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        vm.Venta = new FilaVentaCambio(anterior);
        await EsperarCargaAsync(vm);
        var entrada = Assert.Single(vm.Entradas);
        entrada.Cantidad = 1;
        entrada.Apta = false;
        entrada.Motivo = "Costura dañada";
        vm.Salidas.Add(new(new OpcionVarianteCambio(v2, "COD-V2", "Pantalón", "4", "Azul", p2, 7), 1));
        vm.Pagos[0].Medio = MediosPago.Transferencia;
        vm.Pagos[0].ImporteTexto = "1.234,00";
        vm.MedioReintegro = MediosPago.Transferencia;
        vm.Motivo = "Devolución autorizada";
        vm.UsuarioAutorizante = "duena";
        vm.SeccionActiva = "historial";
        vm.CodigoSalida = "COD-V2";

        vm.Venta = new FilaVentaCambio(nueva);

        Assert.Empty(vm.Salidas);
        Assert.Single(vm.Pagos);
        Assert.Equal(string.Empty, vm.Pagos[0].ImporteTexto);
        Assert.Equal(MediosPago.Efectivo, vm.Pagos[0].Medio);
        Assert.Null(vm.MedioReintegro);
        Assert.Equal(string.Empty, vm.Motivo);
        Assert.Equal(string.Empty, vm.UsuarioAutorizante);
        Assert.False(vm.ContrasenaAutorizanteIngresada);
        Assert.Equal(string.Empty, vm.CodigoSalida);
        Assert.Equal("operacion", vm.SeccionActiva);
        Assert.Empty(vm.Historial);

        await EsperarCargaAsync(vm);
        Assert.Equal(nueva.Id, vm.Venta!.Resumen.Id);
        Assert.All(vm.Entradas, x => Assert.Equal(v2, x.Item.Linea.VarianteId));
        Assert.Empty(vm.Salidas);
    }

    [Fact]
    public async Task Venta_anulada_muestra_estado_y_no_habilita_edicion_ni_registro()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await VenderAsync(e, v1, precio, "anulada");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        await e.Servicios.CasoAnularVenta.EjecutarAsync(resumen.Id, "Venta duplicada", "anular-para-cambio", e.Sesion);
        resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);

        Assert.True(vm.VentaAnulada);
        Assert.Contains("anulada", vm.EstadoVacioOperacion, StringComparison.OrdinalIgnoreCase);
        Assert.False(vm.PuedeEditarOperacion);
        Assert.False(vm.PuedeRegistrarOperacion);
    }

    [Fact]
    public async Task Diferencia_controla_liquidacion_motivo_y_validez_de_registro()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await VenderAsync(e, v1, precio, "estados");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var opcion = await e.Servicios.ConsultasCambios.ObtenerVarianteExactaAsync(await e.CodigoDeAsync(v1), e.Sesion);
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        var entrada = Assert.Single(vm.Entradas);
        entrada.Cantidad = 1;

        Assert.True(vm.DiferenciaNegativa);
        Assert.Contains("A reintegrar", vm.ResultadoDiferenciaTexto);
        Assert.True(vm.ErrorMotivo.Length > 0);
        Assert.False(vm.PuedeRegistrarOperacion);
        Assert.True(vm.PuedeContinuarOperacion);
        vm.Motivo = "La prenda no era adecuada";
        Assert.True(vm.PuedeRegistrarOperacion);
        Assert.True(vm.PuedeContinuarOperacion);

        var salida = new FilaSalidaCambio(opcion!, 1);
        vm.Salidas.Add(salida);
        Assert.True(vm.DiferenciaCero);
        Assert.Equal("Sin diferencia", vm.ResultadoDiferenciaTexto);
        Assert.DoesNotContain("A cobrar ARS 0", vm.ResultadoDiferenciaTexto);
        Assert.True(vm.MuestraAvisoSinDiferencia);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMotivo));
        Assert.True(vm.PuedeRegistrarOperacion);
        Assert.True(vm.PuedeContinuarOperacion);

        salida.Cantidad = 2;
        Assert.True(vm.DiferenciaPositiva);
        Assert.Contains("A cobrar", vm.ResultadoDiferenciaTexto);
        Assert.True(vm.PuedeContinuarOperacion);
        Assert.False(vm.PuedeRegistrarOperacion);

        entrada.Cantidad = entrada.Item.CantidadDisponible + 1;
        Assert.True(entrada.CantidadExcedeDisponible);
        Assert.Contains("Supera el máximo", entrada.ErrorCantidad);
        Assert.False(vm.PuedeContinuarOperacion);
        entrada.Cantidad = 1;
        Assert.False(entrada.CantidadExcedeDisponible);
        Assert.Equal(string.Empty, entrada.ErrorCantidad);
        Assert.True(vm.PuedeContinuarOperacion);

        vm.Pagos[0].ImporteTexto = PosViewModel.FormatearArs(precio);
        Assert.True(vm.Pagos[0].EsEfectivo);
        Assert.Null(vm.Pagos[0].RecibidoMinor);
        Assert.False(vm.PuedeRegistrarOperacion);
        vm.Pagos[0].RecibidoTexto = PosViewModel.FormatearArs(precio);
        Assert.True(vm.PuedeRegistrarOperacion);

        vm.Pagos[0].Medio = MediosPago.Transferencia;
        Assert.False(vm.Pagos[0].EsEfectivo);
        Assert.Null(vm.Pagos[0].RecibidoMinor);
        Assert.True(vm.PuedeRegistrarOperacion);
        Assert.DoesNotContain(vm.Medios, x => x.Valor == MediosPago.MercadoPago);
    }

    [Fact]
    public async Task Articulo_nuevo_se_quita_solo_del_estado_local_y_se_refleja_en_la_diferencia()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await VenderAsync(e, v1, precio, "quitar-salida");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var opcion = await e.Servicios.ConsultasCambios.ObtenerVarianteExactaAsync(await e.CodigoDeAsync(v1), e.Sesion);
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        Assert.Equal("Seleccioná las prendas que vuelven", vm.ResultadoDiferenciaTexto);
        vm.Entradas[0].Cantidad = 1;
        var salida = new FilaSalidaCambio(opcion!, 1);
        vm.Salidas.Add(salida);
        Assert.Equal(0, vm.Diferencia);

        vm.QuitarSalidaCommand.Execute(salida);

        Assert.Empty(vm.Salidas);
        Assert.Equal(-precio, vm.Diferencia);
        Assert.DoesNotContain(vm.Salidas, x => x.Opcion.VarianteId == opcion!.VarianteId);
    }

    [Fact]
    public void Presentacion_no_ofrece_point_para_cobro_y_documenta_reembolso_separado()
    {
        var pagos = new FilaPagoCambio();
        pagos.ImporteTexto = "14.900,00";
        pagos.RecibidoTexto = "20.000,00";

        Assert.Equal(1_490_000L, pagos.ImporteMinor);
        Assert.Equal(2_000_000L, pagos.RecibidoMinor);
        Assert.Contains(PresentacionMedioPagoPos.Todas, x => x.Texto == "Débito — terminal externo");
        Assert.Contains(PresentacionMedioPagoPos.Todas, x => x.Texto == "Crédito — terminal externo");
        Assert.Contains(PresentacionMedioPagoPos.Todas, x => x.Texto == "Billetera / QR — cobro externo");
    }

    [Fact]
    public async Task Busqueda_reactiva_filtra_ventas_sin_descartar_el_borrador_ni_la_seleccion()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "reactiva-a");
        await VenderAsync(e, v2, await e.PrecioDeAsync(v2), "reactiva-b");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        await vm.BuscarAsyncPublic();
        Assert.Equal(2, vm.Ventas.Count);

        var primera = Assert.Single(vm.Ventas, x => x.Numero == "T-000001");
        vm.Venta = primera;
        await EsperarCargaAsync(vm);
        vm.CodigoSalida = "COD-BORRADOR";
        vm.Motivo = "borrador en curso";
        var entradasAlEscribir = vm.Entradas.Count;

        vm.Filtro = "T-000002";
        await EsperarVentasAsync(vm, () => vm.Ventas.Count == 1);

        Assert.Same(primera, vm.Venta);
        Assert.Equal("COD-BORRADOR", vm.CodigoSalida);
        Assert.Equal("borrador en curso", vm.Motivo);
        Assert.Equal(entradasAlEscribir, vm.Entradas.Count);
        Assert.Equal("T-000002", Assert.Single(vm.Ventas).Numero);

        vm.Filtro = string.Empty;
        await EsperarVentasAsync(vm, () => vm.Ventas.Count == 2);
        Assert.Contains(vm.Ventas, x => x.Numero == "T-000001");
    }

    [Fact]
    public async Task Busqueda_por_fecha_acota_los_resultados_y_se_restaura_al_limpiar()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "fecha-a");
        await VenderAsync(e, v2, await e.PrecioDeAsync(v2), "fecha-b");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        await vm.BuscarAsyncPublic();
        Assert.Equal(2, vm.Ventas.Count);

        vm.Desde = DateTime.Today.AddYears(-5);
        vm.Hasta = DateTime.Today.AddYears(-4);
        await EsperarVentasAsync(vm, () => vm.Ventas.Count == 0);

        vm.Desde = null;
        vm.Hasta = null;
        await EsperarVentasAsync(vm, () => vm.Ventas.Count == 2);
    }

    [Fact]
    public async Task Buscador_de_articulos_nuevos_resuelve_precio_y_stock_vigente()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        await vm.MotorArticulos.TextoCambiadoAsync("Remera");
        var porNombre = Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v1);
        Assert.True(porNombre.TienePrecio);
        Assert.Equal(10_000L, porNombre.PrecioMinor);
        Assert.Equal(10L, porNombre.Disponible);
        Assert.False(porNombre.SinStock);
        Assert.Equal("10 en stock", porNombre.StockTexto);

        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v1));
        var porCodigo = Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v1);
        Assert.Equal(10_000L, porCodigo.PrecioMinor);
    }

    [Fact]
    public async Task Buscador_marca_variantes_sin_stock_y_sin_precio_vigente()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var producto = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo F12", null, "cat-rem"), e.Sesion);
        var sinStock = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "L", null), e.Sesion);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(sinStock, 5000, e.Sesion);
        var sinPrecio = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "XL", null), e.Sesion);
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        await vm.MotorArticulos.TextoCambiadoAsync("Buzo F12");

        var agotado = Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == sinStock);
        Assert.True(agotado.SinStock);
        Assert.Equal(0L, agotado.Disponible);
        Assert.False(agotado.SinPrecio);
        Assert.Equal("Sin stock", agotado.StockTexto);

        var sinPrecioVigente = Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == sinPrecio);
        Assert.True(sinPrecioVigente.SinPrecio);
        Assert.False(sinPrecioVigente.TienePrecio);
        Assert.Null(sinPrecioVigente.PrecioMinor);
    }

    [Fact]
    public void Vista_ancla_el_pie_de_operacion_y_ofrece_buscador_de_articulos_con_stock()
    {
        var xaml = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "CambiosView.xaml"));
        var xml = XDocument.Parse(xaml);

        var columnaDerecha = xml.Descendants().Single(e => e.Name.LocalName == "Grid"
            && (string?)e.Attribute("Grid.Column") == "1"
            && e.Elements().Any(c => c.Name.LocalName == "Grid.RowDefinitions"));
        Assert.Equal(new[] { "Auto", "*" },
            columnaDerecha.Elements().Single(e => e.Name.LocalName == "Grid.RowDefinitions")
                .Elements().Select(f => (string?)f.Attribute("Height")).ToArray());

        var operacion = xml.Descendants().Single(e => e.Name.LocalName == "TabItem" && (string?)e.Attribute("Tag") == "operacion");
        var contenedor = operacion.Elements().Single(e => e.Name.LocalName == "Grid");
        Assert.Equal(new[] { "Auto", "Auto", "*", "Auto", "Auto", "Auto" },
            contenedor.Elements().Single(e => e.Name.LocalName == "Grid.RowDefinitions")
                .Elements().Select(f => (string?)f.Attribute("Height")).ToArray());
        // Sin ScrollViewer de página: la grilla de prendas tiene scroll propio (fila *).
        Assert.DoesNotContain(contenedor.Elements(), e => e.Name.LocalName == "ScrollViewer");
        var pie = contenedor.Elements().Single(e => e.Name.LocalName == "Border" && (string?)e.Attribute("Grid.Row") == "5");
        Assert.Contains("Continuar", pie.ToString(SaveOptions.DisableFormatting));
        Assert.Contains("PuedeContinuarOperacion", pie.ToString(SaveOptions.DisableFormatting));

        Assert.Contains("views:BuscadorAsistido", xaml);
        Assert.Contains("Modo=\"VarianteStock\"", xaml);
        Assert.Contains("MotorArticulos", xaml);

        var buscador = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "BuscadorAsistido.xaml"));
        Assert.Contains("PlantillaVarianteStock", buscador);
        Assert.Contains("StockTexto", buscador);
        Assert.Contains("PrecioMinor", buscador);
        var codigo = File.ReadAllText(Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views", "BuscadorAsistido.xaml.cs"));
        Assert.Contains("VarianteStock", codigo);
    }

    [Fact]
    public async Task Primera_entrada_autoselecciona_la_venta_y_carga_su_detalle()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "primera-entrada");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        await vm.BuscarAsyncPublic();

        var venta = Assert.IsType<FilaVentaCambio>(vm.Venta);
        Assert.Equal("T-000001", venta.Resumen.Numero);
        Assert.Same(venta, vm.Ventas[0]);
        await EsperarCargaAsync(vm);
        Assert.False(vm.MostrarEstadoVacioOperacion);
        Assert.True(vm.MostrarOperacion);
        Assert.False(vm.CargandoDetalle);
    }

    [Fact]
    public async Task Cabecera_y_prendas_pertenecen_a_la_misma_venta()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "misma-a");
        await VenderAsync(e, v2, await e.PrecioDeAsync(v2), "misma-b");
        var ventas = await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion);
        var primera = Assert.Single(ventas, x => x.Numero == "T-000001");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        vm.Venta = new FilaVentaCambio(primera);
        await EsperarCargaAsync(vm);

        Assert.Contains("T-000001", vm.VentaContexto);
        Assert.All(vm.Entradas, x => Assert.Equal(v1, x.Item.Linea.VarianteId));
    }

    [Fact]
    public async Task Asignar_la_venta_actualiza_el_estado_vacio_sin_reenlazar_la_vista()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "estado-vacio");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        Assert.True(vm.MostrarEstadoVacioOperacion);
        var notificadas = new List<string>();
        vm.PropertyChanged += (_, a) => { if (a.PropertyName is not null) notificadas.Add(a.PropertyName); };

        vm.Venta = new FilaVentaCambio(resumen);

        Assert.False(vm.MostrarEstadoVacioOperacion);
        Assert.Contains(nameof(CambiosViewModel.MostrarEstadoVacioOperacion), notificadas);
        Assert.Contains(nameof(CambiosViewModel.EstadoVacioOperacion), notificadas);
        await EsperarCargaAsync(vm);
        Assert.True(vm.MostrarOperacion);
    }

    [Fact]
    public async Task Cambio_rapido_de_venta_no_muestra_detalle_obsoleto()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "rapido-a");
        await VenderAsync(e, v2, await e.PrecioDeAsync(v2), "rapido-b");
        var ventas = await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion);
        var r1 = Assert.Single(ventas, x => x.Numero == "T-000001");
        var r2 = Assert.Single(ventas, x => x.Numero == "T-000002");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);

        vm.Venta = new FilaVentaCambio(r1);
        vm.Venta = new FilaVentaCambio(r2);
        await EsperarCargaAsync(vm);

        Assert.Equal(r2.Id, vm.Venta!.Resumen.Id);
        Assert.Contains("T-000002", vm.VentaContexto);
        Assert.All(vm.Entradas, x => Assert.Equal(v2, x.Item.Linea.VarianteId));
    }

    [Fact]
    public async Task Reactivacion_de_la_vista_conserva_el_borrador_de_la_venta_en_curso()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "reactivar-a");
        await VenderAsync(e, v2, await e.PrecioDeAsync(v2), "reactivar-b");
        var ventas = await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion);
        var r1 = Assert.Single(ventas, x => x.Numero == "T-000001");
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(r1);
        await EsperarCargaAsync(vm);
        vm.Motivo = "borrador persistente";

        await vm.BuscarAsyncPublic();

        Assert.Equal(r1.Id, vm.Venta!.Resumen.Id);
        Assert.Equal("borrador persistente", vm.Motivo);
        await EsperarCargaAsync(vm);
        Assert.All(vm.Entradas, x => Assert.Equal(v1, x.Item.Linea.VarianteId));
    }

    [Fact]
    public async Task Agregar_usa_la_variante_seleccionada_y_no_el_texto()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "usa-seleccion");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;
        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v2));
        var sugerencia = Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v2);

        vm.SeleccionarArticuloCommand.Execute(sugerencia);
        Assert.Equal(v2, vm.ArticuloSeleccionado!.VarianteId);
        vm.CodigoSalida = await e.CodigoDeAsync(v1);
        vm.CantidadSalida = 1;

        vm.AgregarSalidaCommand.Execute(null);

        var salida = Assert.Single(vm.Salidas);
        Assert.Equal(v2, salida.Opcion.VarianteId);
        Assert.Equal(12_000L, salida.Opcion.PrecioMinor);
    }

    [Fact]
    public async Task Agregar_dos_articulos_consecutivos_limpia_solo_el_selector_y_restablece_cantidad()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "consecutivos");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;

        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v2));
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v2));
        vm.CantidadSalida = 2;
        vm.AgregarSalidaCommand.Execute(null);

        Assert.Single(vm.Salidas);
        Assert.Equal(v2, vm.Salidas[0].Opcion.VarianteId);
        Assert.Equal(2, vm.Salidas[0].Cantidad);
        Assert.Null(vm.ArticuloSeleccionado);
        Assert.Equal(1, vm.CantidadSalida);
        Assert.Equal(string.Empty, vm.MotorArticulos.Texto);

        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v1));
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v1));
        vm.AgregarSalidaCommand.Execute(null);

        Assert.Equal(2, vm.Salidas.Count);
        Assert.Equal(v1, vm.Salidas[1].Opcion.VarianteId);
        Assert.Equal(1, vm.Salidas[1].Cantidad);
        Assert.Equal(1, vm.CantidadSalida);
    }

    [Fact]
    public async Task Editar_el_texto_del_buscador_invalida_la_seleccion()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "invalida");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;
        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v2));
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v2));
        Assert.True(vm.TieneArticuloSeleccionado);

        await vm.MotorArticulos.TextoCambiadoAsync("texto distinto");

        Assert.Null(vm.ArticuloSeleccionado);
        Assert.False(vm.TieneArticuloSeleccionado);
        Assert.Equal(string.Empty, vm.CodigoSalida);
        vm.AgregarSalidaCommand.Execute(null);
        Assert.Empty(vm.Salidas);
        Assert.Equal("Elegí un artículo de las sugerencias.", vm.Error);
    }

    [Fact]
    public async Task Agregar_valida_cantidad_stock_y_precio_de_la_variante()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var producto = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo validaciones", null, "cat-rem"), e.Sesion);
        var sinPrecio = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "XL", null), e.Sesion);
        var productoAgotado = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo agotado", null, "cat-rem"), e.Sesion);
        var agotado = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(productoAgotado, "L", null), e.Sesion);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(agotado, 5000, e.Sesion);
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "validaciones");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;

        await vm.MotorArticulos.TextoCambiadoAsync("Buzo validaciones");
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == sinPrecio));
        vm.CantidadSalida = 1;
        Assert.False(vm.PuedeAgregarArticulo);
        vm.AgregarSalidaCommand.Execute(null);
        Assert.Empty(vm.Salidas);
        Assert.Contains("precio", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sinPrecio, vm.ArticuloSeleccionado!.VarianteId);
        Assert.Equal(1, vm.CantidadSalida);

        await vm.MotorArticulos.TextoCambiadoAsync("Buzo agotado");
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == agotado));
        vm.CantidadSalida = 1;
        Assert.False(vm.PuedeAgregarArticulo);
        vm.AgregarSalidaCommand.Execute(null);
        Assert.Empty(vm.Salidas);
        Assert.Contains("disponibles", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(agotado, vm.ArticuloSeleccionado!.VarianteId);
        Assert.Equal(1, vm.CantidadSalida);

        await vm.MotorArticulos.TextoCambiadoAsync(await e.CodigoDeAsync(v2));
        vm.SeleccionarArticuloCommand.Execute(Assert.Single(vm.MotorArticulos.Sugerencias, x => x.VarianteId == v2));
        vm.CantidadSalida = 0;
        vm.AgregarSalidaCommand.Execute(null);
        Assert.Contains("mayor que cero", vm.Error, StringComparison.OrdinalIgnoreCase);

        vm.CantidadSalida = 99;
        vm.AgregarSalidaCommand.Execute(null);
        Assert.Contains("disponibles", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.Salidas);
    }

    [Fact]
    public async Task El_modo_no_conserva_articulos_ocultos_que_afecten_los_importes()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        await VenderAsync(e, v1, await e.PrecioDeAsync(v1), "modo-ocultos");
        var resumen = Assert.Single(await e.Servicios.ConsultasCambios.BuscarVentasAsync("", null, null, e.Sesion));
        var opcion = await e.Servicios.ConsultasCambios.ObtenerVarianteExactaAsync(await e.CodigoDeAsync(v2), e.Sesion);
        var vm = new CambiosViewModel(e.Servicios, () => e.Sesion, true, true);
        vm.Venta = new FilaVentaCambio(resumen);
        await EsperarCargaAsync(vm);
        vm.Entradas[0].Cantidad = 1;

        var salida = new FilaSalidaCambio(opcion!, 1);
        vm.Salidas.Add(salida);
        Assert.True(vm.TotalSalidas > 0);
        vm.ModoOperacion = "devolucion";

        Assert.Equal("cambio", vm.ModoOperacion);
        Assert.Single(vm.Salidas);
        Assert.Contains("Quitá los artículos nuevos", vm.Error);

        vm.QuitarSalidaCommand.Execute(salida);
        vm.ModoOperacion = "devolucion";

        Assert.Equal("devolucion", vm.ModoOperacion);
        Assert.Empty(vm.Salidas);
        Assert.Equal(0, vm.TotalSalidas);
        Assert.Equal(-vm.TotalEntradas, vm.Diferencia);
    }

    private static async Task EsperarVentasAsync(CambiosViewModel vm, Func<bool> condicion)
    {
        for (var i = 0; i < 500; i++)
        {
            if (!vm.BuscandoVentas && condicion()) return;
            await Task.Delay(10);
        }

        Assert.True(condicion(), $"La búsqueda de ventas no convergió (Ventas={vm.Ventas.Count}, Buscando={vm.BuscandoVentas}).");
    }

    private static async Task VenderAsync(VentasTests.EntornoVentas e, string variante, long precio, string sufijo)
    {
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-cambios-{sufijo}-{Guid.NewGuid():N}", e.Sesion);
    }

    private static async Task EsperarCargaAsync(CambiosViewModel vm)
    {
        for (var i = 0; i < 300 && (vm.CargandoDetalle || vm.Entradas.Count == 0); i++)
            await Task.Delay(10);
        Assert.False(vm.CargandoDetalle);
        Assert.NotEmpty(vm.Entradas);
    }

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
