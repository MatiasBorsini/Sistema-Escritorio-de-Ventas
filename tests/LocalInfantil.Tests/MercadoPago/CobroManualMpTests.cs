using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>
/// F12-B: modalidades de Point. Integrado (flujo F12 vigente), manual (cobro
/// declarado, atómico, sin API) y desactivado (Point no se ofrece). Sin
/// doble cobro, sin presentar el manual como remoto verificado.
/// </summary>
public sealed class CobroManualMpTests
{
    private static IReadOnlyList<NuevaLineaVenta> Lineas(string variante, long precio) => new[] { new NuevaLineaVenta(variante, 1, precio) };

    private sealed class DialogosFake : IDialogosVentas
    {
        public bool Respuesta { get; set; }
        public int Consultas { get; private set; }
        public void MostrarTicket(string ventaId) { }
        public bool ConfirmarCobroManualDuplicado(string detalle) { Consultas++; return Respuesta; }
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condicion()) return;
            await Task.Delay(25);
        }
    }

    private static async Task<long> ContarAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task EjecutarSqlAsync(string ruta, string sql)
    {
        await using var conexion = new SqliteConnection($"Data Source={ruta};Pooling=false");
        await conexion.OpenAsync();
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Integrado_sigue_disponible_para_cobrar_en_point()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        Assert.Equal(ModosPoint.Integrado, e.Servicios.ObtenerModoPoint());

        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null, true, false, false, e.Iniciar, e.Consultar, e.Cancelar);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;

        Assert.Contains(vm.MediosDisponibles, o => o.Valor == MediosPago.MercadoPago);
        Assert.True(vm.MostrarAccionPointIntegrada);
        Assert.False(vm.MostrarAccionPointManual);
    }

    [Fact]
    public async Task Desactivado_no_ofrece_point()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Desactivado);

        var vm = new PosViewModel(e.Servicios, () => e.Sesion);
        Assert.Equal(ModosPoint.Desactivado, vm.ModoPoint);
        Assert.DoesNotContain(vm.MediosDisponibles, o => o.Valor == MediosPago.MercadoPago);
        Assert.False(vm.MostrarAccionPointIntegrada);
        Assert.False(vm.MostrarAccionPointManual);
    }

    [Fact]
    public async Task Manual_registra_venta_sin_llamar_a_la_api()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        var resultado = await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-manual-1", "0001", e.Sesion);
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(resultado);

        Assert.Empty(e.Fake.Requests);
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_operacion;"));
    }

    [Fact]
    public async Task Manual_persiste_origen_responsable_y_estado_pendiente()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.RegistrarManual.EjecutarAsync(
            Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-manual-2", "REF-77", e.Sesion);

        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var cobro = await e.Servicios.RepoMercadoPago.ObtenerCobroManualPorVentaAsync(s, ok.VentaId);
        Assert.NotNull(cobro);
        Assert.Equal(EstadosConciliacionCobroManualMp.Pendiente, cobro!.EstadoConciliacion);
        Assert.Equal("REF-77", cobro.ComprobanteRef);
        Assert.Equal("FUTURO_X1__SN000001", cobro.TerminalId);
        Assert.False(string.IsNullOrWhiteSpace(cobro.UsuarioId));
        Assert.Equal(precio, cobro.ImporteMinor);
    }

    [Fact]
    public async Task Manual_idempotente_una_sola_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-manual-dup", null, e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-manual-dup", null, e.Sesion));

        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_requiere_exactamente_un_pago_point()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[]
        {
            new NuevoPago(MediosPago.MercadoPago, precio, null),
            new NuevoPago(MediosPago.MercadoPago, precio, null),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-manual-doble", null, e.Sesion));
        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_no_habilitado_en_modo_integrado()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-manual-no", null, e.Sesion));
        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
    }

    [Fact]
    public async Task Manual_pago_mixto_efectivo_y_point()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[]
        {
            new NuevoPago(MediosPago.Efectivo, 4000, 5000),
            new NuevoPago(MediosPago.MercadoPago, precio - 4000, null),
        };

        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-manual-mixto", null, e.Sesion);
        Assert.Equal(precio, ok.TotalMinor);
        Assert.Empty(e.Fake.Requests);
    }

    [Fact]
    public async Task Manual_no_registra_point_como_efectivo_en_caja()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-manual-caja", null, e.Sesion);

        Assert.Equal(0, await ContarAsync(e.RutaDb, "SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta' AND medio='efectivo';"));
        Assert.Equal(precio, await ContarAsync(e.RutaDb, $"SELECT COALESCE(SUM(importe_minor),0) FROM caja_movimiento WHERE tipo='venta' AND medio='mercado_pago';"));
    }

    [Fact]
    public async Task Manual_no_permite_reembolso_remoto()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.RegistrarManual.EjecutarAsync(
            Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-manual-refund", null, e.Sesion);

        await using var s = await e.Servicios.Fabrica.IniciarLecturaAsync();
        var pago = (await e.Servicios.RepoPagos.ListarPorVentaAsync(s, ok.VentaId)).Single(p => p.Medio == MediosPago.MercadoPago);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Reembolsar.EjecutarAsync(ok.VentaId, pago.Id, precio, LocalInfantil.Application.Features.Autenticacion.PermisosComerciales.ReintegroAutorizar, e.Sesion));
        Assert.Contains("No hay cobro Point confirmado", ex.Message);
        Assert.Empty(e.Fake.Requests);
    }

    [Fact]
    public async Task Reinicio_conserva_modalidad()
    {
        using var e = await EntornoMp.CrearAsync();
        await e.Configurar.CambiarModoAsync(ModosPoint.Manual, e.Sesion);

        var servicios2 = new LocalInfantil.Wpf.Infra.ServiciosComerciales(e.RutaDb);
        Assert.Equal(ModosPoint.Manual, servicios2.ObtenerModoPoint());
    }

    [Fact]
    public async Task Cobro_incierto_bloquea_cambio_de_modalidad()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.ResponderTerminales("PDV");
        e.Fake.Responder("POST", "/v1/orders", (_, _) => throw new HttpRequestException("sin respuesta"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Iniciar.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, e.Sesion));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Configurar.CambiarModoAsync(ModosPoint.Manual, e.Sesion));
        Assert.Contains("pendientes", ex.Message);
    }

    [Fact]
    public async Task Pos_manual_registra_sin_red_ni_segundo_cobro()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, _, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        var vm = new PosViewModel(e.Servicios, () => e.Sesion);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        vm.Pagos[0].ImporteTexto = (precio / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.False(vm.PuedeRegistrarCobroManual);
        vm.ConfirmacionManualPointAceptada = true;
        Assert.True(vm.PuedeRegistrarCobroManual);

        vm.RegistrarCobroManualCommand.Execute(null);
        for (var i = 0; i < 200; i++)
        {
            var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
            if (ventas.Count == 1) break;
            await Task.Delay(25);
        }

        Assert.Empty(e.Fake.Requests);
        Assert.Single(await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_advierte_duplicado_mismo_importe_y_exige_confirmacion()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-dup-1", null, e.Sesion);

        var ex = await Assert.ThrowsAsync<CobroManualDuplicadoSospechosoException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-dup-2", null, e.Sesion));
        Assert.NotEmpty(ex.Candidatos);

        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_permite_segunda_venta_con_confirmacion_humana()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-seg-1", null, e.Sesion);
        var ok = await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-seg-2", null, e.Sesion, confirmarPosibleDuplicado: true);

        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_importe_distinto_no_advierte_duplicado()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-imp-1", null, e.Sesion);
        var ok = await e.RegistrarManual.EjecutarAsync(new[] { new NuevaLineaVenta(v1, 2, precio) }, new[] { new NuevoPago(MediosPago.MercadoPago, precio * 2, null) }, "op-imp-2", null, e.Sesion);

        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Pos_manual_cancelado_ante_duplicado_no_registra()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-pos-prev", null, e.Sesion);

        var dialogos = new DialogosFake { Respuesta = false };
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, dialogos);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        vm.Pagos[0].ImporteTexto = (precio / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.ConfirmacionManualPointAceptada = true;

        vm.RegistrarCobroManualCommand.Execute(null);
        await EsperarAsync(() => Task.FromResult(dialogos.Consultas > 0));

        Assert.Equal(1, dialogos.Consultas);
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
        Assert.Contains("duplicado", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pos_manual_confirmado_ante_duplicado_registra_segunda_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-pos-prev2", null, e.Sesion);

        var dialogos = new DialogosFake { Respuesta = true };
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, dialogos);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        vm.Pagos[0].ImporteTexto = (precio / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.ConfirmacionManualPointAceptada = true;

        vm.RegistrarCobroManualCommand.Execute(null);
        await EsperarAsync(async () => await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;") == 2);

        Assert.Equal(1, dialogos.Consultas);
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
    }

    // --- Revisión final F12-B02: bordes del dedup manual ---

    [Fact]
    public async Task Manual_advierte_duplicado_aunque_el_primero_ya_este_conciliado()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-conc-1", null, e.Sesion);
        await EjecutarSqlAsync(e.RutaDb, "UPDATE mercado_pago_cobro_manual SET estado_conciliacion='conciliada';");

        await Assert.ThrowsAsync<CobroManualDuplicadoSospechosoException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-conc-2", null, e.Sesion));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_no_reutiliza_comprobante_en_otra_operacion()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-ref-1", "TICKET-ABC", e.Sesion);

        var pagosDos = new[] { new NuevoPago(MediosPago.MercadoPago, precio * 2, null) };
        var lineasDos = new[] { new NuevaLineaVenta(v1, 2, precio) };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(lineasDos, pagosDos, "op-ref-2", "TICKET-ABC", e.Sesion));
        Assert.Contains("comprobante", ex.Message, StringComparison.OrdinalIgnoreCase);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(lineasDos, pagosDos, "op-ref-3", "TICKET-ABC", e.Sesion, confirmarPosibleDuplicado: true));

        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_confirmaciones_simultaneas_registran_una_sola_vez()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        async Task IntentarAsync()
        {
            try { await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-sim", null, e.Sesion); }
            catch { /* la segunda confirmación debe fallar por idempotencia */ }
        }

        await Task.WhenAll(IntentarAsync(), IntentarAsync());

        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }

    [Fact]
    public async Task Manual_no_omite_la_proteccion_de_cobro_integrado_pendiente_al_confirmar()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-prot-1", null, e.Sesion);

        e.EstablecerModo(ModosPoint.Integrado);
        e.ResponderTerminales("PDV");
        e.Fake.Responder("POST", "/v1/orders", (_, _) => throw new HttpRequestException("sin respuesta"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Iniciar.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, e.Sesion));

        e.EstablecerModo(ModosPoint.Manual);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) }, "op-prot-2", null, e.Sesion, confirmarPosibleDuplicado: true));
        Assert.Contains("pendiente de resolver", ex.Message);
        Assert.Equal(1, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
    }

    [Fact]
    public async Task Manual_permite_dos_cobros_legitimos_con_comprobantes_distintos()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.EstablecerModo(ModosPoint.Manual);
        var pagos = new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) };

        await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-leg-1", "TK-1", e.Sesion);
        var ok = await e.RegistrarManual.EjecutarAsync(Lineas(v1, precio), pagos, "op-leg-2", "TK-2", e.Sesion, confirmarPosibleDuplicado: true);

        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM venta;"));
        Assert.Equal(2, await ContarAsync(e.RutaDb, "SELECT COUNT(*) FROM mercado_pago_cobro_manual;"));
    }
}
