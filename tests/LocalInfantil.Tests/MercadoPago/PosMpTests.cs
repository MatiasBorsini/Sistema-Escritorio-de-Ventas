using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §43: doble click/Enter concurrente → una sola operación lógica.</summary>
public sealed class PosMpTests
{
    [Fact]
    public async Task Seleccionar_point_precarga_el_total_sin_llamar_a_la_api()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null, true, false, false, e.Iniciar, e.Consultar, e.Cancelar);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);

        vm.Pagos[0].Medio = MediosPago.MercadoPago;

        Assert.Equal(vm.Total, vm.Pagos[0].ImporteMinor);
        Assert.Equal($"Cobrar {PosViewModel.FormatearArs(vm.Total)} en Point", vm.TextoAccionPoint);
        Assert.Empty(e.Fake.Requests);
        Assert.False(vm.PuedeConfirmar);
    }

    [Fact]
    public async Task Cobro_point_incierto_bloquea_edicion_de_la_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        _ = await e.CatalogoConStockAsync();
        e.ResponderTerminales();
        e.Fake.Responder("POST", "/v1/orders", (_, _) => throw new HttpRequestException("sin respuesta"));
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null, true, false, false, e.Iniciar, e.Consultar, e.Cancelar);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;

        vm.CobrarEnPointCommand.Execute(null);
        for (var i = 0; i < 200 && !e.Fake.Requests.Any(r => r.Metodo == "POST" && r.Url == "/v1/orders"); i++)
            await Task.Delay(25);
        for (var i = 0; i < 200 && !vm.OperacionPointIncierta; i++)
            await Task.Delay(25);

        Assert.True(vm.OperacionPointIncierta);
        Assert.False(vm.PuedeEditarVenta);
        Assert.False(vm.PuedeEditarModalidad);
        Assert.False(vm.PuedeCancelarVenta);
        Assert.False(vm.PuedeCobrarPoint);
        Assert.Contains("no está confirmado", vm.EstadoCobroMp ?? string.Empty);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task Doble_cobro_concurrente_una_sola_order()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null, true, false, false, e.Iniciar, e.Consultar, e.Cancelar);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        var filaMp = vm.Pagos[0];
        filaMp.Medio = MediosPago.MercadoPago;
        filaMp.ImporteTexto = (precio / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(vm.PuedeCobrarPoint);
        Assert.False(vm.PuedeConfirmar);

        e.ResponderOrden("ORD000001", "processed");
        vm.CobrarEnPointCommand.Execute(null);
        vm.CobrarEnPointCommand.Execute(null);
        for (var i = 0; i < 200; i++)
        {
            var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
            if (ventas.Count == 1) break;
            await Task.Delay(50);
        }
        var posts = e.Fake.Requests.Where(r => r.Metodo == "POST" && r.Url == "/v1/orders").ToList();
        Assert.Single(posts);
        var final = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Single(final);
    }

    [Fact]
    public async Task Doble_enter_durante_espera_una_sola_order_una_key_una_venta()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        var estadoRemoto = "created";
        e.Fake.Responder("GET", "/v1/orders/ORD000001", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            $"{{\"id\":\"ORD000001\",\"type\":\"point\",\"status\":\"{estadoRemoto}\",\"external_reference\":\"x\",\"transactions\":{{\"payments\":[]}}}}"));
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null, true, false, false, e.Iniciar, e.Consultar, e.Cancelar);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        vm.Pagos[0].ImporteTexto = (precio / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(vm.PuedeCobrarPoint);

        vm.CobrarEnPointCommand.Execute(null);
        for (var i = 0; i < 200; i++)
        {
            if (e.Fake.Requests.Any(r => r.Metodo == "POST" && r.Url == "/v1/orders")) break;
            await Task.Delay(50);
        }
        Assert.True(vm.CobroEnCurso);
        Assert.False(vm.PuedeEditarVenta);
        Assert.False(vm.PuedeCancelarVenta);
        vm.CobrarEnPointCommand.Execute(null);
        estadoRemoto = "processed";
        for (var i = 0; i < 200; i++)
        {
            var ventas = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
            if (ventas.Count == 1) break;
            await Task.Delay(50);
        }
        var posts = e.Fake.Requests.Where(r => r.Metodo == "POST" && r.Url == "/v1/orders").ToList();
        Assert.Single(posts);
        Assert.Single(posts.Select(r => r.IdempotencyKey).Distinct());
        var final = await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion);
        Assert.Single(final);
    }

    [Fact]
    public async Task Confirmar_directo_con_mp_redirige_a_point()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        _ = (v1, precio);
        var vm = new PosViewModel(e.Servicios, () => e.Sesion, null);
        var sug = (await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("REM", 10, e.Sesion)).First();
        await vm.AgregarSugerenciaAsyncPublic(sug);
        vm.Pagos[0].Medio = MediosPago.MercadoPago;
        vm.Pagos[0].ImporteTexto = "100";
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("Cobrar en Point", vm.Error ?? string.Empty);
        Assert.Empty(await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion));
    }
}
