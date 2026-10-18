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
        Assert.Contains("El cambio registra el crédito local. El reembolso de Mercado Pago es una operación remota separada.", xaml);
        Assert.Contains("Reembolsar en Mercado Pago", xaml);
        Assert.Contains("DiferenciaPositiva", xaml);
        Assert.Contains("DiferenciaNegativa", xaml);
        Assert.Contains("MuestraAvisoSinDiferencia", xaml);
        Assert.DoesNotContain("Importe a imputar", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#", xaml);
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
        vm.Motivo = "La prenda no era adecuada";
        Assert.True(vm.PuedeRegistrarOperacion);

        var salida = new FilaSalidaCambio(opcion!, 1);
        vm.Salidas.Add(salida);
        Assert.True(vm.DiferenciaCero);
        Assert.Equal("Sin diferencia", vm.ResultadoDiferenciaTexto);
        Assert.DoesNotContain("A cobrar ARS 0", vm.ResultadoDiferenciaTexto);
        Assert.True(vm.MuestraAvisoSinDiferencia);
        Assert.True(string.IsNullOrEmpty(vm.ErrorMotivo));
        Assert.True(vm.PuedeRegistrarOperacion);

        salida.Cantidad = 2;
        Assert.True(vm.DiferenciaPositiva);
        Assert.Contains("A cobrar", vm.ResultadoDiferenciaTexto);
        Assert.False(vm.PuedeRegistrarOperacion);
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

    private static async Task VenderAsync(VentasTests.EntornoVentas e, string variante, long precio, string sufijo)
    {
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)],
            [new NuevoPago(MediosPago.Efectivo, precio, precio)],
            $"op-cambios-{sufijo}-{Guid.NewGuid():N}", e.Sesion);
    }

    private static async Task EsperarCargaAsync(CambiosViewModel vm)
    {
        for (var i = 0; i < 300 && (vm.Ocupado || vm.Entradas.Count == 0); i++)
            await Task.Delay(10);
        Assert.False(vm.Ocupado);
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
