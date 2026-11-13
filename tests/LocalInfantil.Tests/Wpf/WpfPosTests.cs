using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// F04-T01/T02: carrito, pagos y confirmación desde el POS.
/// </summary>
public sealed class WpfPosTests
{
    [Fact]
    public async Task Agregar_por_sugerencia_y_codigo_exacto_incrementa_fila()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        var solicitoFoco = false;
        vm.SolicitarFocoBuscador += () => solicitoFoco = true;
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(e.Codigo1, 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        Assert.Null(vm.Error);
        Assert.True(vm.Filas.Count == 1);

        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        Assert.Null(vm.Error);
        var fila = Assert.Single(vm.Filas);
        Assert.Equal(2, fila.Cantidad);
        Assert.Equal(2 * 10000, vm.Total);
        Assert.True(solicitoFoco);
    }

    [Fact]
    public async Task Cantidad_mayor_al_disponible_se_rechaza_con_mensaje_humano()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(e.Codigo2, 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        var fila = Assert.Single(vm.Filas);

        fila.CantidadTexto = "999";
        Assert.Contains("Solo hay 7 unidades disponibles", vm.Error ?? string.Empty);
        Assert.Equal(1, fila.Cantidad);
    }

    [Fact]
    public async Task Sin_precio_vigente_no_agrega()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.CodigoSinPrecio);
        Assert.Equal("Esta variante no tiene precio vigente.", vm.Error);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task Escaneo_repetido_incrementa_una_sola_fila_y_devuelve_foco()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        var foco = 0;
        vm.SolicitarFocoBuscador += () => foco++;
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        var fila = Assert.Single(vm.Filas);
        Assert.Equal(3, fila.Cantidad);
        Assert.Equal(30000, vm.Total);
        Assert.Equal(3, foco);
    }

    [Fact]
    public async Task Codigo_inexistente_muestra_codigo_y_reinicia_el_buscador()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic("REM-999-99");
        Assert.Equal("No encontramos el código REM-999-99.", vm.Error);
        Assert.Empty(vm.Filas);
        Assert.Equal(string.Empty, vm.MotorSugerencias.Texto);
    }

    [Fact]
    public async Task Busqueda_con_varias_coincidencias_no_agrega_la_primera_sin_elegir()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);

        await vm.AgregarDirectaAsyncPublic("Remera POS");

        Assert.Empty(vm.Filas);
        Assert.Equal("Hay varias coincidencias. Elegí una variante de la lista.", vm.Error);
        Assert.Equal(4, vm.MotorSugerencias.Sugerencias.Count);
        Assert.Equal("Remera POS", vm.MotorSugerencias.Texto);
    }

    [Fact]
    public async Task Codigo_exacto_sigue_agregandose_directamente()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);

        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        Assert.Equal(e.V1, Assert.Single(vm.Filas).VarianteId);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Nueva_venta_no_descarta_carrito_sin_confirmacion()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);

        vm.NuevaVentaCommand.Execute(null);

        Assert.Single(vm.Filas);
        Assert.True(vm.ConfirmacionCancelarVisible);
    }

    [Fact]
    public async Task Cobro_se_abre_como_paso_explicito_y_puede_volver_al_carrito()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);

        Assert.False(vm.CobroVisible);
        Assert.False(vm.PuedeAbrirCobro);
        Assert.False(vm.MostrarCtaCobrar);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        Assert.True(vm.PuedeAbrirCobro);
        Assert.True(vm.MostrarCtaCobrar);

        vm.AbrirCobroCommand.Execute(null);
        Assert.True(vm.CobroVisible);
        Assert.False(vm.MostrarCtaCobrar);
        Assert.True(vm.PuedeCerrarCobro);
        vm.CerrarCobroCommand.Execute(null);

        Assert.False(vm.CobroVisible);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task Cobro_precarga_total_en_un_medio_y_respeta_ediciones_al_agregar_otro()
    {
        using var e = await EntornoPos.CrearAsync();
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(e.V1, 2_490_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);

        vm.Pagos[0].Medio = MediosPago.Transferencia;
        Assert.Equal("24.900,00", vm.Pagos[0].ImporteTexto);
        vm.AbrirCobroCommand.Execute(null);
        Assert.Equal("24.900,00", vm.Pagos[0].ImporteTexto);
        Assert.False(vm.Pagos[0].ImporteModificadoPorUsuario);

        vm.Pagos[0].Medio = MediosPago.Efectivo;
        vm.Pagos[0].ImporteTexto = "10.000,00";
        vm.Pagos[0].RecibidoTexto = "10.000,00";
        await vm.AgregarPagoAsync();

        Assert.Equal("10.000,00", vm.Pagos[0].ImporteTexto);
        Assert.Equal("14.900,00", vm.Pagos[1].ImporteTexto);
        Assert.Equal(MediosPago.Efectivo, vm.Pagos[0].Medio);
        vm.Pagos[1].Medio = MediosPago.Transferencia;
        Assert.Equal("14.900,00", vm.Pagos[1].ImporteTexto);
        Assert.Equal("10.000,00", vm.Pagos[0].ImporteTexto);
        Assert.True(vm.PuedeConfirmar);
    }

    [Fact]
    public async Task POS_muestra_y_registra_cobro_parcial_mas_deuda_solo_por_el_restante()
    {
        using var e = await EntornoPos.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente con cuenta", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 20_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos, puedeVenderFiado: true, puedeConsultarCuenta: true);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SeleccionarClienteCommand.Execute(new SugerenciaCliente(cliente, "Cliente con cuenta", null));
        await EsperarLimiteAsync(vm, 20_000);
        vm.AbrirCobroCommand.Execute(null);
        vm.EsFiado = true;
        var pago = Assert.Single(vm.Pagos);
        pago.Medio = MediosPago.Transferencia;
        pago.ImporteTexto = "40,00";

        Assert.Equal(10_000, vm.Total);
        Assert.Equal(4_000, vm.CobradoAhoraMinor);
        Assert.Equal(6_000, vm.DeudaNuevaMinor);
        Assert.Equal(vm.Total, vm.CobradoAhoraMinor + vm.DeudaNuevaMinor);
        Assert.Equal(0, vm.SaldoClienteMinor);
        Assert.Equal(6_000, vm.SaldoPosteriorMinor);
        Assert.Equal(20_000, vm.LimiteClienteMinor);
        Assert.Equal(14_000, vm.DisponibleCreditoPosteriorMinor);
        Assert.Contains("Pago parcial", vm.TextoEstadoPago);
        Assert.True(vm.MostrarResumenCuentaCobro);
        Assert.True(vm.MostrarDistribucionPago);
        Assert.True(vm.PuedeConfirmar);

        await vm.ConfirmarAsyncPublic();

        Assert.Null(vm.Error);
        Assert.Equal(6_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        var movimientoVenta = Assert.Single(caja.Movimientos.Where(m => m.Tipo == TiposMovimientoCaja.Venta));
        Assert.Equal(4_000, movimientoVenta.ImporteMinor);
    }

    [Fact]
    public async Task POS_permite_venta_totalmente_a_cuenta_sin_pago_ficticio()
    {
        using var e = await EntornoPos.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente venta a cuenta", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 20_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos, puedeVenderFiado: true, puedeConsultarCuenta: true);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SeleccionarClienteCommand.Execute(new SugerenciaCliente(cliente, "Cliente venta a cuenta", null));
        await EsperarLimiteAsync(vm, 20_000);
        vm.AbrirCobroCommand.Execute(null);
        var pago = Assert.Single(vm.Pagos);

        // La apertura precarga efectivo. La elección explícita de fiado total
        // informa y limpia la distribución para que no quede detrás de los campos ocultos.
        Assert.Equal(vm.Total, pago.ImporteMinor);
        pago.RecibidoTexto = "100,00";
        vm.ElegirFiadoTotalCommand.Execute(null);
        pago = Assert.Single(vm.Pagos);

        Assert.Equal(10_000, vm.Total);
        Assert.True(vm.EsFiado);
        Assert.Equal(string.Empty, pago.ImporteTexto);
        Assert.Equal(string.Empty, pago.RecibidoTexto);
        Assert.Equal(0, vm.CobradoAhoraMinor);
        Assert.Equal(10_000, vm.DeudaNuevaMinor);
        Assert.Equal(vm.Total, vm.CobradoAhoraMinor + vm.DeudaNuevaMinor);
        Assert.Contains("Toda la venta queda a cuenta", vm.TextoEstadoPago);
        Assert.Contains("Toda la venta queda a cuenta", vm.EstadoCuentaCorriente);
        Assert.True(vm.MostrarResumenCuentaCobro);
        Assert.False(vm.MostrarDistribucionPago);
        Assert.True(vm.MostrarOpcionCobroParcial);
        vm.MostrarCobroParcialCommand.Execute(null);
        Assert.True(vm.MostrarDistribucionPago);
        Assert.Equal(string.Empty, pago.ImporteTexto);
        Assert.Equal(0, vm.CobradoAhoraMinor);

        // Volver a parcial parte de cero y calcula la deuda como diferencia.
        pago.Medio = MediosPago.Transferencia;
        pago.ImporteTexto = "40,00";
        Assert.Equal(4_000, vm.CobradoAhoraMinor);
        Assert.Equal(6_000, vm.DeudaNuevaMinor);
        Assert.Equal(vm.Total, vm.CobradoAhoraMinor + vm.DeudaNuevaMinor);

        // Volver a fiado total limpia la distribución parcial; nada oculto se confirma.
        vm.ElegirFiadoTotalCommand.Execute(null);
        pago = Assert.Single(vm.Pagos);
        Assert.Equal(0, vm.CobradoAhoraMinor);
        Assert.Equal(vm.Total, vm.DeudaNuevaMinor);
        Assert.False(vm.MostrarDistribucionPago);
        Assert.True(vm.PuedeConfirmar);

        await vm.ConfirmarAsyncPublic();

        Assert.Null(vm.Error);
        Assert.Equal(10_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.DoesNotContain(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Venta);
    }

    [Fact]
    public async Task Cliente_sin_limite_no_puede_elegir_fiado_y_conserva_pago_normal()
    {
        using var e = await EntornoPos.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente sin fiado", null, null), e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos, puedeVenderFiado: true, puedeConsultarCuenta: true);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SeleccionarClienteCommand.Execute(new SugerenciaCliente(cliente, "Cliente sin fiado", null));
        for (var i = 0; i < 200 && vm.CargandoResumenCuenta; i++) await Task.Delay(10);
        vm.AbrirCobroCommand.Execute(null);

        Assert.False(vm.MostrarOpcionesFiado);
        Assert.True(vm.MostrarAvisoFiadoNoDisponible);
        Assert.Contains("Fiado no habilitado", vm.MotivoFiadoNoDisponible);
        vm.ElegirFiadoTotalCommand.Execute(null);
        vm.MostrarCobroParcialCommand.Execute(null);
        Assert.False(vm.EsFiado);
        Assert.False(vm.MostrarResumenCuentaCobro);
        Assert.True(vm.MostrarDistribucionPago);
        var pago = Assert.Single(vm.Pagos);
        pago.RecibidoTexto = pago.ImporteTexto;
        Assert.True(vm.PuedeConfirmar);
    }

    [Fact]
    public async Task Efectivo_insuficiente_no_se_convierte_silenciosamente_en_deuda_y_no_muestra_pago_completo()
    {
        using var e = await EntornoPos.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente con cuenta", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 20_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos, puedeVenderFiado: true, puedeConsultarCuenta: true);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SeleccionarClienteCommand.Execute(new SugerenciaCliente(cliente, "Cliente con cuenta", null));
        await EsperarLimiteAsync(vm, 20_000);
        vm.AbrirCobroCommand.Execute(null);
        vm.EsFiado = true;
        var pago = Assert.Single(vm.Pagos);
        pago.ImporteTexto = "100,00";
        pago.RecibidoTexto = "20,00";

        Assert.Equal(8_000, pago.ImporteMinor!.Value - pago.RecibidoMinor!.Value);
        Assert.Contains("Faltan ARS 80,00", vm.TextoEstadoPago);
        Assert.Contains("no pasa automáticamente a cuenta", vm.TextoEstadoPago);
        Assert.Equal(string.Empty, vm.TextoVuelto);
        Assert.DoesNotContain("Pago completo", vm.TextoEstadoPago);
        Assert.Equal(0, vm.DeudaNuevaMinor);
        Assert.False(vm.PuedeConfirmar);
    }

    [Fact]
    public async Task Credito_insuficiente_muestra_una_advertencia_clara_y_no_un_disponible_negativo()
    {
        using var e = await EntornoPos.CrearAsync();
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(e.V1, 1_000_000, e.Sesion);
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente con límite menor", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 500_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos, puedeVenderFiado: true, puedeConsultarCuenta: true);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SeleccionarClienteCommand.Execute(new SugerenciaCliente(cliente, "Cliente con límite menor", null));
        await EsperarLimiteAsync(vm, 500_000);
        vm.AbrirCobroCommand.Execute(null);
        vm.EsFiado = true;
        var pago = Assert.Single(vm.Pagos);
        pago.Medio = MediosPago.Transferencia;
        pago.ImporteTexto = string.Empty;

        Assert.Contains("Crédito insuficiente", vm.TextoEstadoPago);
        Assert.Contains("disponibles", vm.TextoEstadoPago);
        Assert.Contains("Cobrá al menos", vm.TextoEstadoPago);
        Assert.Equal(0, vm.DisponibleCreditoPosteriorMostradoMinor);
        Assert.False(vm.PuedeConfirmar);

        pago.ImporteTexto = "5.000,00";
        Assert.Equal(500_000, vm.DeudaNuevaMinor);
        Assert.True(vm.PuedeConfirmar);
    }

    [Fact]
    public void Vista_POS_presenta_cobro_a_cuenta_con_resumen_compacto_y_sin_alertas_duplicadas()
    {
        var raiz = Path.Combine(RaizRepo(), "src", "LocalInfantil.Wpf", "Views");
        var carrito = File.ReadAllText(Path.Combine(raiz, "PosView.xaml"));
        var cobro = File.ReadAllText(Path.Combine(raiz, "CobrarVentaWindow.xaml"));
        var inicioCobro = carrito.IndexOf("<!-- El cobro se conserva", StringComparison.Ordinal);
        var pantallaCarrito = inicioCobro < 0 ? carrito : carrito[..inicioCobro];

        Assert.Contains("Resumen de la venta", pantallaCarrito);
        Assert.Contains("Continuar al cobro", pantallaCarrito);
        Assert.DoesNotContain("Medios para cobrar ahora", pantallaCarrito);
        Assert.DoesNotContain("Vencimiento (opcional)", pantallaCarrito);

        Assert.Contains("Paga todo ahora", cobro);
        Assert.Contains("Toda la venta a cuenta", cobro);
        Assert.Contains("Pago parcial y resto a cuenta", cobro);
        Assert.Equal(3, cobro.Split("Style=\"{StaticResource EstiloTarjetaModalidad}\"").Length - 1);
        Assert.Contains("Property=\"IsChecked\" Value=\"True\"", cobro);
        Assert.Contains("MostrarOpcionesFiado", cobro);
        Assert.Contains("MotivoFiadoNoDisponible", cobro);
        Assert.Contains("A cobrar ahora:", cobro);
        Assert.Contains("Deuda nueva:", cobro);
        Assert.Contains("Saldo anterior", cobro);
        Assert.Contains("Saldo posterior", cobro);
        Assert.Contains("Límite autorizado", cobro);
        Assert.Contains("Crédito disponible después:", cobro);
        Assert.Contains("Vencimiento (opcional)", cobro);
        Assert.DoesNotContain("Cobrado ahora:", cobro);
        Assert.DoesNotContain("El efectivo recibido debe cubrir el importe aplicado.", cobro);
        Assert.Contains("DisponibleCreditoPosteriorMostradoMinor", cobro);
        Assert.DoesNotContain("E8BB", cobro);

        var comoPaga = cobro.IndexOf("¿Cómo paga el cliente?", StringComparison.Ordinal);
        var medios = cobro.IndexOf("Medios para cobrar ahora", StringComparison.Ordinal);
        var resumen = cobro.IndexOf("Resumen del cobro", StringComparison.Ordinal);
        var confirmar = cobro.IndexOf("Content=\"Confirmar venta\"", StringComparison.Ordinal);
        Assert.True(comoPaga >= 0 && comoPaga < medios && medios < resumen && resumen < confirmar);
    }

    [Fact]
    public async Task Point_precarga_total_o_saldo_restante_sin_alterar_el_pago_existente()
    {
        using var e = await EntornoPos.CrearAsync();
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(e.V1, 2_490_000, e.Sesion);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);

        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        Assert.Equal("24.900,00", vm.Pagos[0].ImporteTexto);
        Assert.Equal("Cobrar ARS 24.900,00 en Point", vm.TextoAccionPoint);
        Assert.False(vm.PuedeConfirmar);

        vm.Pagos[0].Medio = MediosPago.Efectivo;
        vm.Pagos[0].ImporteTexto = "10.000,00";
        vm.Pagos[0].RecibidoTexto = "10.000,00";
        await vm.AgregarPagoAsync();
        var segunda = vm.Pagos[1];
        segunda.Medio = MediosPago.MercadoPago;

        Assert.Equal("10.000,00", vm.Pagos[0].ImporteTexto);
        Assert.Equal("14.900,00", segunda.ImporteTexto);
        Assert.True(vm.PuedeCobrarPoint);
        Assert.False(vm.PuedeConfirmar);
        Assert.True(vm.MostrarAyudaPoint);
    }

    [Fact]
    public void Selector_pos_explica_medios_externos_y_no_ofrece_cuotas()
    {
        var opciones = PresentacionMedioPagoPos.Todas;
        Assert.Equal("Efectivo", opciones.Single(x => x.Valor == MediosPago.Efectivo).Texto);
        Assert.Equal("Transferencia", opciones.Single(x => x.Valor == MediosPago.Transferencia).Texto);
        Assert.Equal("Mercado Pago Point", opciones.Single(x => x.Valor == MediosPago.MercadoPago).Texto);
        Assert.Equal("Débito — terminal externo", opciones.Single(x => x.Valor == MediosPago.Debito).Texto);
        Assert.Equal("Crédito — terminal externo", opciones.Single(x => x.Valor == MediosPago.Credito).Texto);
        Assert.Equal("Billetera / QR — cobro externo", opciones.Single(x => x.Valor == MediosPago.Billetera).Texto);

        foreach (var medio in new[] { MediosPago.Debito, MediosPago.Credito, MediosPago.Billetera })
        {
            var ayuda = opciones.Single(x => x.Valor == medio).Ayuda;
            Assert.Equal("Este cobro se realiza fuera de LocalInfantil. Aquí solo se registra el importe.", ayuda);
            Assert.DoesNotContain("cuota", ayuda!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Agregar_medio_con_saldo_cero_no_propone_importe_negativo()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.Pagos[0].ImporteTexto = "200,00";

        await vm.AgregarPagoAsync();

        Assert.Equal(string.Empty, vm.Pagos[1].ImporteTexto);
        Assert.Equal("200,00", vm.Pagos[0].ImporteTexto);
    }

    [Fact]
    public async Task Variante_sin_stock_muestra_estado_operativo_y_no_agrega()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.CodigoSinStock);
        Assert.Equal("Sin stock disponible.", vm.Error);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task Confirmar_con_efectivo_exactto_muestra_banner_y_rota_operacion()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        var opAnterior = vm.OperacionId;
        vm.Pagos[0].ImporteTexto = "100,00";
        vm.Pagos[0].RecibidoTexto = "100,00";
        Assert.True(vm.PuedeConfirmar);

        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Equal("T-000001", vm.UltimoNumero);
        Assert.Equal(10000, vm.UltimoTotal);
        Assert.NotEqual(opAnterior, vm.OperacionId);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task Precio_cambiado_actualiza_carrito_y_pide_revision()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(e.V1, 11000, e.Sesion);
        vm.Pagos[0].ImporteTexto = "100,00";
        vm.Pagos[0].RecibidoTexto = "100,00";

        await vm.ConfirmarAsyncPublic();
        Assert.Contains("cambió de", vm.Error ?? string.Empty);
        Assert.Null(vm.UltimoNumero);
        Assert.Equal(11000, vm.Filas[0].PrecioUnitarioMinor);
        Assert.Equal(11000, vm.Total);
    }

    [Fact]
    public async Task Pagos_controlan_confirmacion_y_vuelto()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        Assert.False(vm.PuedeConfirmar);

        vm.Pagos[0].ImporteTexto = "50,00";
        vm.Pagos[0].RecibidoTexto = "50,00";
        Assert.False(vm.PuedeConfirmar);
        Assert.Equal(5000, vm.Restante);

        await vm.AgregarPagoAsync();
        vm.Pagos[1].Medio = MediosPago.Debito;
        vm.Pagos[1].ImporteTexto = "50,00";
        Assert.True(vm.PuedeConfirmar);

        vm.Pagos[0].RecibidoTexto = "80,00";
        Assert.Equal(3000, vm.VueltoTotal);
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Equal("T-000001", vm.UltimoNumero);
    }

    [Fact]
    public void Parseo_acepta_miles_y_rechaza_ambiguo()
    {
        Assert.True(FilaPagoViewModel.TryParseArs("49.900,00", out var miles));
        Assert.Equal(4990000, miles);
        Assert.True(FilaPagoViewModel.TryParseArs("1234,50", out _));
        Assert.False(FilaPagoViewModel.TryParseArs("1,234.50", out _));
        Assert.False(FilaPagoViewModel.TryParseArs("1.234,567", out _));
    }

    [Fact]
    public async Task Cancelar_pide_confirmacion_solo_con_lineas()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        vm.SolicitarCancelarCommand.Execute(null);
        Assert.False(vm.ConfirmacionCancelarVisible);

        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.SolicitarCancelarCommand.Execute(null);
        Assert.True(vm.ConfirmacionCancelarVisible);
        vm.ConfirmarCancelarCommand.Execute(null);
        Assert.Empty(vm.Filas);
        Assert.False(vm.ConfirmacionCancelarVisible);
    }

    [Fact]
    public async Task Ver_ticket_abre_dialogo_con_ultima_venta()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.VerTicketAsyncPublic();
        Assert.Equal("Todavía no hay venta para mostrar.", vm.Error);

        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.Pagos[0].ImporteTexto = "100,00";
        vm.Pagos[0].RecibidoTexto = "100,00";
        await vm.ConfirmarAsyncPublic();
        await vm.VerTicketAsyncPublic();
        Assert.NotNull(e.Dialogos.UltimoTicket);
    }

    [Fact]
    public async Task Boton_mas_y_menos_actualizan_cantidad_visible_subtotal_y_total()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        var fila = Assert.Single(vm.Filas);
        var notificoCantidadTexto = false;
        fila.PropertyChanged += (_, a) =>
        {
            if (a.PropertyName == nameof(FilaVentaViewModel.CantidadTexto)) notificoCantidadTexto = true;
        };

        Assert.Equal("1", fila.CantidadTexto);
        vm.IncrementarCommand.Execute(fila);

        Assert.Equal(2, fila.Cantidad);
        Assert.True(notificoCantidadTexto, "El botón + cambió Cantidad sin notificar CantidadTexto: la celda queda desincronizada.");
        Assert.Equal("2", fila.CantidadTexto);
        Assert.Equal(20000, fila.SubtotalMinor);
        Assert.Equal(2, vm.CantidadUnidades);
        Assert.Equal("1 variante · 2 unidades", vm.ResumenCarrito);
        Assert.Equal(20000, vm.Total);

        vm.DecrementarCommand.Execute(fila);
        Assert.Equal(1, fila.Cantidad);
        Assert.Equal("1", fila.CantidadTexto);
        Assert.Equal(10000, fila.SubtotalMinor);
        Assert.Equal(1, vm.CantidadUnidades);
        Assert.Equal(10000, vm.Total);
    }

    [Fact]
    public async Task Sin_efectivo_recibido_no_muestra_sin_vuelto()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.AbrirCobroCommand.Execute(null);
        var pago = Assert.Single(vm.Pagos);
        Assert.Equal("100,00", pago.ImporteTexto);

        Assert.True(pago.SolicitaRecibido);
        Assert.Contains("Ingresá el efectivo recibido", vm.TextoEstadoPago);
        Assert.Equal(string.Empty, vm.TextoVuelto);

        pago.RecibidoTexto = "100,00";
        Assert.False(pago.SolicitaRecibido);
        Assert.Equal("Sin vuelto", vm.TextoVuelto);
    }

    [Fact]
    public async Task Modificar_carrito_con_cobro_abierto_mantiene_y_valida_los_pagos()
    {
        using var e = await EntornoPos.CrearAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.AgregarDirectaAsyncPublic(e.Codigo1);
        vm.AbrirCobroCommand.Execute(null);

        // Nueva fila con cobro abierto: el importe automático sigue al total.
        await vm.AgregarDirectaAsyncPublic(e.Codigo2);
        var pago = Assert.Single(vm.Pagos);
        Assert.Equal("220,00", pago.ImporteTexto);

        // Más unidades: la precarga automática recalcula; sin sobrescribir lo manual.
        var fila = vm.Filas.First(f => f.CodigoInterno == e.Codigo1);
        vm.IncrementarCommand.Execute(fila);
        Assert.Equal("320,00", pago.ImporteTexto);
        Assert.Equal("2 variantes · 3 unidades", vm.ResumenCarrito);
        Assert.Equal(20000, fila.SubtotalMinor);

        pago.RecibidoTexto = "320,00";
        Assert.True(vm.PuedeConfirmar);

        // Importe editado a mano por el operador no se sobrescribe.
        pago.ImporteTexto = "3,00";
        vm.IncrementarCommand.Execute(fila);
        Assert.Equal("3,00", pago.ImporteTexto);
        Assert.False(vm.PuedeConfirmar);
        Assert.Contains("Falta cobrar", vm.TextoEstadoPago);
    }

    private static async Task EsperarLimiteAsync(PosViewModel vm, long esperado)
    {
        for (var i = 0; i < 200 && vm.LimiteClienteMinor != esperado; i++)
            await Task.Delay(10);
        Assert.Equal(esperado, vm.LimiteClienteMinor);
    }

    private static string RaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "LocalInfantil.sln"))) return dir.FullName;
        throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private sealed class DialogosVentasFake : IDialogosVentas
    {
        public string? UltimoTicket { get; private set; }
        public bool ConfirmarDuplicado { get; set; } = true;
        public int ConsultasDuplicado { get; private set; }
        public void MostrarTicket(string ventaId) => UltimoTicket = ventaId;
        public bool ConfirmarCobroManualDuplicado(string detalle) { ConsultasDuplicado++; return ConfirmarDuplicado; }
    }

    private sealed class EntornoPos : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public DialogosVentasFake Dialogos { get; } = new();
        public string Codigo1 { get; }
        public string Codigo2 { get; }
        public string CodigoSinPrecio { get; }
        public string CodigoSinStock { get; }
        public string V1 { get; }
        private readonly string _dir;

        private EntornoPos(string dir, ServiciosComerciales servicios, string sesion, string c1, string c2, string csp, string css, string v1)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            Codigo1 = c1;
            Codigo2 = c2;
            CodigoSinPrecio = csp;
            CodigoSinStock = css;
            V1 = v1;
        }

        public static async Task<EntornoPos> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "pos-f04.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            // F12-B: instalación con terminal ya configurada → modalidad
            // integrada por defecto (el POS ofrece el cobro Point como antes).
            servicios.ConfigMercadoPagoStore.Guardar(new ConfigMercadoPago("FUTURO_X1__SN000001", "PDV", null, null, null, null));
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCatalogo.ProductosGestionar,
                PermisosCatalogo.VariantesGestionar,
                PermisosCatalogo.PreciosGestionar,
                PermisosInventario.CargaInicialEstablecer,
                PermisosVentas.VentasConfirmar,
                PermisosVentas.VentasAnular,
            })
            {
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            }

            await servicios.CasoAbrirCaja.EjecutarAsync(0, $"op-apertura-test-{Guid.NewGuid():N}", sesion.IdSesion);

            var prod = await servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera POS", null, "cat-rem"), sesion.IdSesion);
            var v1 = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), sesion.IdSesion);
            var v2 = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "S", null), sesion.IdSesion);
            var v3 = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "L", null), sesion.IdSesion);
            var v4 = await servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "XL", null), sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(v1, 10000, sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(v2, 12000, sesion.IdSesion);
            await servicios.CasoFijarPrecio.EjecutarAsync(v4, 9000, sesion.IdSesion);
            await servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(v1, 10), new FilaCargaInicial(v2, 7), new FilaCargaInicial(v3, 4) },
                Guid.NewGuid().ToString("N"),
                sesion.IdSesion);
            var c1 = (await servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, sesion.IdSesion))!.CodigoInterno;
            var c2 = (await servicios.ConsultasCatalogo.ObtenerVarianteAsync(v2, sesion.IdSesion))!.CodigoInterno;
            var c3 = (await servicios.ConsultasCatalogo.ObtenerVarianteAsync(v3, sesion.IdSesion))!.CodigoInterno;
            var c4 = (await servicios.ConsultasCatalogo.ObtenerVarianteAsync(v4, sesion.IdSesion))!.CodigoInterno;
            return new EntornoPos(dir, servicios, sesion.IdSesion, c1, c2, c3, c4, v1);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
