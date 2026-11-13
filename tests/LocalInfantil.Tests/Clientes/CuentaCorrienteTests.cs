using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Tests.Ventas;

namespace LocalInfantil.Tests.Clientes;

public sealed class CuentaCorrienteTests
{
    [Fact]
    public async Task Venta_parcial_persiste_solo_la_deuda_y_el_efectivo_recibido_en_caja()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 15_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        var pagado = precio / 2;
        var efectivo = pagado / 2;
        var debito = pagado - efectivo;

        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [new(MediosPago.Efectivo, efectivo, efectivo + 500), new(MediosPago.Debito, debito, null)], "cc-venta-parcial", e.Sesion,
            cliente, "2020-01-01", true));

        var resumen = await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion);
        Assert.Equal(precio - pagado, resumen!.SaldoMinor);
        var ticket = await e.Servicios.CasoGenerarTicket.EjecutarAsync(ok.VentaId, e.Sesion);
        Assert.Equal(precio - pagado, ticket!.ImporteCuentaCorrienteMinor);
        Assert.Equal(pagado, ticket.ImportePagadoMinor);
        Assert.Equal("2020-01-01", ticket.VencimientoCuentaCorriente);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(2, caja.ResumenMedios.Count);
        Assert.Contains(caja.ResumenMedios, p => p.Medio == MediosPago.Efectivo && p.ImporteMinor == efectivo);
        Assert.Contains(caja.ResumenMedios, p => p.Medio == MediosPago.Debito && p.ImporteMinor == debito);
        Assert.Equal(efectivo, caja.EfectivoEsperadoMinor);
    }

    [Fact]
    public async Task Ajustes_exigen_motivo_y_no_pueden_crear_saldo_a_favor()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente ajustes", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 50_000, e.Sesion);
        var preview = await e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, true, 7_000, e.Sesion);
        Assert.Equal(0, preview.SaldoAnteriorMinor);
        await Assert.ThrowsAsync<ArgumentException>(() => e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(
            new(cliente, true, 7_000, " ", 0, "cc-ajuste-sin-motivo", e.Sesion)));
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(cliente, true, 7_000, "Corrección autorizada", 0, "cc-ajuste-positivo", e.Sesion));
        var previoReduccion = await e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, false, 3_000, e.Sesion);
        Assert.Equal(4_000, previoReduccion.SaldoPosteriorMinor);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(cliente, false, 3_000, "Diferencia revisada", 7_000, "cc-ajuste-negativo", e.Sesion));
        Assert.Equal(4_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, false, 4_001, e.Sesion));
        var empleada = await e.SesionEmpleadaAsync();
        await Assert.ThrowsAsync<LocalInfantil.Application.Abstractions.AccesoDenegadoException>(() => e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, true, 1_000, empleada));
        await Assert.ThrowsAsync<LocalInfantil.Application.Abstractions.AccesoDenegadoException>(() => e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, 30_000, empleada));
    }

    [Fact]
    public async Task Ajustes_requieren_fiado_habilitado_pero_abonos_conservan_deuda_historica()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 30_000);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(
            new(cliente, true, 10_000, "Deuda previa", 0, "cc-deuda-previa-habilitada", e.Sesion));
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, null, e.Sesion);

        var errorPreviewAumento = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, true, 1_000, e.Sesion));
        Assert.Contains("fiado no está habilitado", errorPreviewAumento.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoAjustarCuentaCorriente.PrevisualizarAsync(cliente, false, 1_000, e.Sesion));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(
            new(cliente, true, 1_000, "No debe aumentar", 10_000, "cc-ajuste-sin-fiado-pos", e.Sesion)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(
            new(cliente, false, 1_000, "No debe ajustar", 10_000, "cc-ajuste-sin-fiado-neg", e.Sesion)));
        Assert.Equal(10_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Null((await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.LimiteMinor);

        var pagos = new[] { new NuevoPago(MediosPago.Efectivo, 4_000, 4_000) };
        var previewAbono = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, 4_000, pagos, e.Sesion);
        await e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(
            cliente, 4_000, pagos, previewAbono.Aplicaciones, previewAbono.SaldoAnteriorMinor, "cc-abono-fiado-deshabilitado", e.Sesion));

        var final = await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion);
        Assert.Equal(6_000, final!.SaldoMinor);
        Assert.Null(final.LimiteMinor);
    }

    [Fact]
    public async Task Anular_venta_totalmente_fiada_cancela_deuda_sin_reintegro_de_caja()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 20_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        var codigo = await e.CodigoDeAsync(variante);
        var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-venta-anulable", e.Sesion, cliente, null, true));
        Assert.Equal(9, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(codigo, e.Sesion))!.Disponible);
        var ticket = await e.Servicios.CasoGenerarTicket.EjecutarAsync(venta.VentaId, e.Sesion);
        Assert.Equal(precio, ticket!.ImporteCuentaCorrienteMinor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-venta-anulable", e.Sesion, cliente, null, true));
        Assert.Equal(precio, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Equal(9, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(codigo, e.Sesion))!.Disponible);
        var before = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos.Count;
        await e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Anulación solicitada", "cc-anular-fiado", e.Sesion);
        Assert.Equal(0, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Equal(10, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(codigo, e.Sesion))!.Disponible);
        Assert.Equal(before, (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos.Count);
        Assert.Equal("anulada", (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!.Venta.Estado);
    }

    [Fact]
    public async Task Fiado_requiere_cliente_permiso_limite_y_no_admite_exceder_limite()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente sin límite", null, null), e.Sesion);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-sin-limite", e.Sesion, cliente, null, true));
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, precio - 1, e.Sesion);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-limite-excedido", e.Sesion, cliente, null, true));
        Assert.Equal("Esta operación supera el límite de cuenta corriente del cliente.", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-sin-cliente", e.Sesion, null, null, true));

        var empleadoId = await e.Servicios.CasoCrearUsuario.EjecutarAsync(new("empleada-fiado", "empleada"), "clave-empleada-0000", e.Sesion);
        var empleada = await e.Servicios.CasoIniciarSesion.EjecutarAsync("empleada-fiado", "clave-empleada-0000");
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, precio * 2, e.Sesion);
        await Assert.ThrowsAsync<LocalInfantil.Application.Abstractions.AccesoDenegadoException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-empleada-denegada", empleada.IdSesion, cliente, null, true));
        await e.Servicios.CasoConfigurarPermiso.EjecutarAsync(empleadoId, PermisosCuentaCorriente.VenderFiado, true, e.Sesion);
        var vendida = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [], "cc-empleada-concesion", empleada.IdSesion, cliente, null, true));
        Assert.Equal(precio, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Equal(precio, (await e.Servicios.CasoGenerarTicket.EjecutarAsync(vendida.VentaId, e.Sesion))!.ImporteCuentaCorrienteMinor);
    }

    [Fact]
    public async Task Ventas_fiadas_concurrentes_revalidan_el_limite_dentro_de_la_transaccion()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 50_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(cliente, precio, e.Sesion);

        async Task<bool> Vender(string operacion)
        {
            try
            {
                await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(variante, 1, precio)], [], operacion, e.Sesion, cliente, null, true);
                return true;
            }
            catch (InvalidOperationException ex) when (ex.Message == "Esta operación supera el límite de cuenta corriente del cliente.")
            {
                return false;
            }
        }

        var resultados = await Task.WhenAll(Vender("cc-concurrente-1"), Vender("cc-concurrente-2"));
        Assert.Single(resultados.Where(x => x));
        Assert.Single(resultados.Where(x => !x));
        Assert.Equal(precio, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
    }

    [Fact]
    public async Task Abono_combinado_se_aplica_fifo_con_medio_y_recibo_historico()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 50_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(variante, 1, precio)], [], "cc-venta-1", e.Sesion, cliente, null, true);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(variante, 1, precio)], [], "cc-venta-2", e.Sesion, cliente, null, true);

        var pagos = new NuevoPago[] { new(MediosPago.Efectivo, 12_000, 12_500), new(MediosPago.Debito, 8_000, null) };
        var preview = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, 20_000, pagos, e.Sesion);
        Assert.Equal(20_000, preview.SaldoAnteriorMinor);
        Assert.Equal(0, preview.SaldoPosteriorMinor);
        Assert.Equal(2, preview.Aplicaciones.Select(a => a.CargoId).Distinct().Count());
        var abonoId = await e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(cliente, 20_000, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "cc-abono-mix", e.Sesion));
        var ticket = await e.Servicios.CasoGenerarTicketAbono.EjecutarAsync(abonoId, e.Sesion);
        Assert.Equal("A-000001", ticket!.Numero);
        Assert.Equal(2, ticket.Pagos.Count);
        Assert.Equal(new[] { 10_000L, 10_000L }, ticket.Aplicaciones.Select(a => a.ImporteMinor).ToArray());
        Assert.All(ticket.Aplicaciones, a => Assert.Equal(0, a.PendientePosteriorMinor));
        Assert.Equal(0, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Abono && m.ImporteMinor == 12_000 && m.Medio == MediosPago.Efectivo);
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Abono && m.ImporteMinor == 8_000 && m.Medio == MediosPago.Debito);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoRegistrarAbono.EjecutarAsync(
            new(cliente, 20_000, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "cc-abono-mix", e.Sesion)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(cliente, 1, [new(MediosPago.Efectivo, 1, 1)], [], 0, "cc-abono-exceso", e.Sesion)));
    }

    [Fact]
    public async Task Abonos_concurrentes_no_pueden_aplicar_dos_veces_el_mismo_saldo()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 50_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(variante, 1, precio)], [], "cc-doble-abono-venta", e.Sesion, cliente, null, true);
        var pagos = new[] { new NuevoPago(MediosPago.Transferencia, precio, null) };
        var preview1 = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, precio, pagos, e.Sesion);
        var preview2 = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, precio, pagos, e.Sesion);

        async Task<bool> Abonar(string operacion, VistaPreviaAbono preview)
        {
            try
            {
                await e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(cliente, precio, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, operacion, e.Sesion));
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }

        var resultados = await Task.WhenAll(Abonar("cc-doble-abono-1", preview1), Abonar("cc-doble-abono-2", preview2));
        Assert.Single(resultados.Where(x => x));
        Assert.Single(resultados.Where(x => !x));
        Assert.Equal(0, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Single((await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos.Where(m => m.Tipo == TiposMovimientoCaja.Abono));
    }

    [Fact]
    public async Task Empleada_puede_consultar_y_registrar_abonos_por_permiso_predeterminado()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 50_000);
        var empleadoId = await e.Servicios.CasoCrearUsuario.EjecutarAsync(new("empleada-abono", "empleada"), "clave-empleada-0000", e.Sesion);
        var empleada = await e.Servicios.CasoIniciarSesion.EjecutarAsync("empleada-abono", "clave-empleada-0000");
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        await e.Servicios.CasoConfirmarVenta.EjecutarAsync([new(variante, 1, precio)], [], "cc-abono-empleada-venta", e.Sesion, cliente, null, true);
        var pagos = new[] { new NuevoPago(MediosPago.Transferencia, precio, null) };
        var preview = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, precio, pagos, empleada.IdSesion);
        await e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(cliente, precio, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "cc-abono-empleada", empleada.IdSesion));
        Assert.Equal(0, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, empleada.IdSesion))!.SaldoMinor);
        Assert.NotEqual(string.Empty, empleadoId);
    }

    [Fact]
    public async Task Anular_venta_con_abono_combinado_reembolsa_cada_componente_en_su_medio()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 50_000);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        var pagoInicial = precio / 5;
        var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(variante, 1, precio)], [new(MediosPago.Efectivo, pagoInicial, pagoInicial)], "cc-venta-abono-anulable", e.Sesion, cliente, null, true));
        var efectivo = precio / 4;
        var debito = precio / 4;
        var importeAbono = efectivo + debito;
        var pagos = new NuevoPago[] { new(MediosPago.Efectivo, efectivo, efectivo), new(MediosPago.Debito, debito, null) };
        var preview = await e.Servicios.ConsultasCuentaCorriente.PrevisualizarAbonoAsync(cliente, importeAbono, pagos, e.Sesion);
        await e.Servicios.CasoRegistrarAbono.EjecutarAsync(new(cliente, importeAbono, pagos, preview.Aplicaciones, preview.SaldoAnteriorMinor, "cc-abono-anulable", e.Sesion));

        await e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Anulación con abono", "cc-anular-con-abono", e.Sesion);

        Assert.Equal(0, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Reintegro && m.ImporteMinor == -pagoInicial && m.Medio == MediosPago.Efectivo);
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Reintegro && m.ImporteMinor == -efectivo && m.Medio == MediosPago.Efectivo);
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.Reintegro && m.ImporteMinor == -debito && m.Medio == MediosPago.Debito);
    }

    [Fact]
    public async Task Devolucion_con_credito_mayor_a_deuda_aplica_solo_a_venta_original_y_reintegra_resto()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 100_000);
        var (_, original, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(original);
        var pagoInicial = precio / 2;
        var ventaOriginal = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(original, 1, precio)], [new(MediosPago.Efectivo, pagoInicial, pagoInicial)], "cc-cambio-venta-original", e.Sesion, cliente, null, true));
        var otraVenta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(original, 1, precio)], [], "cc-cambio-otra-venta", e.Sesion, cliente, null, true));
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ventaOriginal.VentaId, e.Sesion))!;
        var linea = Assert.Single(ficha.Lineas);
        var datos = new DatosCambio(ventaOriginal.VentaId, [new(linea.Id, 1, true, null)], [], [], null, "Devolución con crédito mayor a deuda", "cc-cambio-mayor-deuda", e.Sesion);

        var requerida = Assert.IsType<ResultadoCambio.DecisionCuentaRequerida>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos));
        Assert.Equal(precio - pagoInicial, requerida.DeudaVentaMinor);
        var vista = Assert.IsType<ResultadoCambio.ConfirmacionCuenta>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.AplicarADeuda, DeudaCuentaEsperadaMinor = requerida.DeudaVentaMinor }));
        Assert.Equal(precio - pagoInicial, vista.AplicarMinor);
        Assert.Equal(0, vista.DeudaPosteriorMinor);
        Assert.Equal(pagoInicial, vista.ReintegrarMinor);
        var cajaAntes = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos.Count;
        await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.AplicarADeuda, ConfirmarDecisionCuenta = true, DeudaCuentaEsperadaMinor = vista.DeudaAnteriorMinor });

        Assert.Equal(precio, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(cajaAntes + 1, caja.Movimientos.Count);
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.CambioReintegro && m.ImporteMinor == -pagoInicial);

        var otraFicha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(otraVenta.VentaId, e.Sesion))!;
        var otraLinea = Assert.Single(otraFicha.Lineas);
        var reintegroDatos = new DatosCambio(otraVenta.VentaId, [new(otraLinea.Id, 1, true, null)], [], [], null, "Reintegro elegido por cliente", "cc-cambio-reintegro", e.Sesion);
        var otraDecision = Assert.IsType<ResultadoCambio.DecisionCuentaRequerida>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(reintegroDatos));
        var reintegroPreview = Assert.IsType<ResultadoCambio.ConfirmacionCuenta>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(reintegroDatos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.Reintegrar, DeudaCuentaEsperadaMinor = otraDecision.DeudaVentaMinor }));
        Assert.Equal(otraDecision.DeudaVentaMinor, reintegroPreview.DeudaPosteriorMinor);
        Assert.Equal(precio, reintegroPreview.ReintegrarMinor);
        await e.Servicios.CasoRegistrarCambio.EjecutarAsync(reintegroDatos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.Reintegrar, ConfirmarDecisionCuenta = true, DeudaCuentaEsperadaMinor = reintegroPreview.DeudaAnteriorMinor });
        Assert.Equal(precio, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Contains(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.CambioReintegro && m.ImporteMinor == -precio);
    }

    [Fact]
    public async Task Devolucion_con_deuda_exige_eleccion_y_aplicar_no_genera_movimiento_de_caja()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var cliente = await ClienteHabilitado(e, 30_000);
        var (_, original, salida) = await e.CatalogoConStockAsync();
        var precioOriginal = await e.PrecioDeAsync(original);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(salida, 5_000, e.Sesion);
        var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new(original, 1, precioOriginal)], [], "cc-venta-devolucion", e.Sesion, cliente, null, true));
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!;
        var entrada = Assert.Single(ficha.Lineas);
        var datos = new DatosCambio(venta.VentaId, [new(entrada.Id, 1, true, null)], [new(salida, 1, 5_000)], [], null, null, "cc-cambio-decision", e.Sesion);

        var requiere = Assert.IsType<ResultadoCambio.DecisionCuentaRequerida>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos));
        Assert.Equal(precioOriginal, requiere.DeudaVentaMinor);
        Assert.Equal(precioOriginal - 5_000, requiere.CreditoDevolucionMinor);
        Assert.Equal(precioOriginal, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        Assert.Empty(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId, e.Sesion));

        var preview = Assert.IsType<ResultadoCambio.ConfirmacionCuenta>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.AplicarADeuda, DeudaCuentaEsperadaMinor = requiere.DeudaVentaMinor }));
        Assert.Equal(0, preview.ReintegrarMinor);
        Assert.Equal(5_000, preview.DeudaPosteriorMinor);
        var registrados = Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos with
        { DecisionCuenta = DecisionCuentaCorrienteCambio.AplicarADeuda, ConfirmarDecisionCuenta = true, DeudaCuentaEsperadaMinor = preview.DeudaAnteriorMinor }));
        Assert.Equal(5_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(cliente, e.Sesion))!.SaldoMinor);
        var ticket = await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(registrados.CambioId, e.Sesion);
        Assert.Equal("aplicar_a_deuda", ticket!.DecisionCuenta);
        Assert.Equal(precioOriginal - 5_000, ticket.ImporteAplicadoDeudaMinor);
        var caja = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.DoesNotContain(caja.Movimientos, m => m.Tipo == TiposMovimientoCaja.CambioReintegro);
    }

    private static async Task<string> ClienteHabilitado(VentasTests.EntornoVentas e, long limite)
    {
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente Cuenta", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(id, limite, e.Sesion);
        return id;
    }
}
