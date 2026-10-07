using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>F12 §51: reembolsos totales/parciales, límites, idempotencia, F06.</summary>
public sealed class ReembolsoMpTests
{
    private static async Task<(EntornoMp E, string VentaId, string PagoId, long Precio)> VentaMpConfirmadaAsync()
    {
        var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        e.ResponderTerminales();
        e.ResponderCrearOrden();
        var op = await e.Iniciar.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.MercadoPago, precio, null) },
            e.Sesion);
        e.ResponderOrden("ORD000001", "processed");
        var final = await e.Consultar.EjecutarAsync(op, e.Sesion);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(final.VentaId!, e.Sesion))!;
        return (e, final.VentaId!, ficha.Pagos.Single(p => p.Medio == MediosPago.MercadoPago).Id, precio);
    }

    [Fact]
    public async Task Reembolso_total_y_parcial_con_limite()
    {
        var (e, ventaId, pagoId, precio) = await VentaMpConfirmadaAsync();
        using (e)
        {
            e.Fake.Responder("POST", "/v1/orders/ORD000001/refund", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
                """{"id":"ORD000001","status":"refunded","transactions":{"payments":[]}}"""));
            var mitad = precio / 2;
            var reop = await e.Reembolsar.EjecutarAsync(ventaId, pagoId, mitad, PermisosComerciales.ReintegroAutorizar, e.Sesion);
            await e.Reembolsar.MarcarConfirmadoLocalAsync(reop, e.Sesion);

            var excedido = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                e.Reembolsar.EjecutarAsync(ventaId, pagoId, precio, PermisosComerciales.ReintegroAutorizar, e.Sesion));
            Assert.Contains("supera el cobro original", excedido.Message);

            var resto = await e.Reembolsar.EjecutarAsync(ventaId, pagoId, precio - mitad, PermisosComerciales.ReintegroAutorizar, e.Sesion);
            await e.Reembolsar.MarcarConfirmadoLocalAsync(resto, e.Sesion);
            var refunds = e.Fake.Requests.Where(r => r.Url.EndsWith("/refund")).ToList();
            Assert.Equal(2, refunds.Count);
            Assert.True(refunds.Select(r => r.IdempotencyKey).Distinct().Count() == 2);
        }
    }

    [Fact]
    public async Task Segundo_reembolso_en_curso_se_rechaza()
    {
        var (e, ventaId, pagoId, precio) = await VentaMpConfirmadaAsync();
        using (e)
        {
            e.Fake.Responder("POST", "/v1/orders/ORD000001/refund", (_, _) => throw new TaskCanceledException("timeout"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                e.Reembolsar.EjecutarAsync(ventaId, pagoId, precio / 2, PermisosComerciales.ReintegroAutorizar, e.Sesion));
            var duplicado = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                e.Reembolsar.EjecutarAsync(ventaId, pagoId, precio / 4, PermisosComerciales.ReintegroAutorizar, e.Sesion));
            Assert.Contains("en curso", duplicado.Message);
        }
    }

    [Fact]
    public async Task Timeout_refund_reconcilia_sin_duplicar()
    {
        var (e, ventaId, pagoId, precio) = await VentaMpConfirmadaAsync();
        using (e)
        {
            e.Fake.Responder("POST", "/v1/orders/ORD000001/refund", (_, _) => throw new TaskCanceledException("timeout"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                e.Reembolsar.EjecutarAsync(ventaId, pagoId, precio, PermisosComerciales.ReintegroAutorizar, e.Sesion));
            e.Fake.Responder("POST", "/v1/orders/ORD000001/refund", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
                """{"id":"ORD000001","status":"refunded","transactions":{"payments":[]}}"""));
            e.ResponderOrden("ORD000001", "processed");
            var resumen = await e.Reconciliar.EjecutarAsync(e.Sesion);
            Assert.Equal(1, resumen.PendientesDeVerificar);
            string reop;
            await using (var s = await e.Servicios.Fabrica.IniciarLecturaAsync())
                reop = (await e.Servicios.RepoMercadoPago.ListarReembolsosPorVentaAsync(s, ventaId, default)).Single().ReembolsoOperacionId;
            await e.Reembolsar.MarcarConfirmadoLocalAsync(reop, e.Sesion);
            var resumen2 = await e.Reconciliar.EjecutarAsync(e.Sesion);
            Assert.Equal(0, resumen2.PendientesDeVerificar);
            var refunds = e.Fake.Requests.Where(r => r.Url.EndsWith("/refund")).ToList();
            Assert.Equal(2, refunds.Count);
            Assert.Single(refunds.Select(r => r.IdempotencyKey).Distinct());
        }
    }

    [Fact]
    public async Task F06_reintegro_mp_sin_cobertura_falla()
    {
        using var e = await EntornoMp.CrearAsync();
        var (_, v1, precio) = await e.CatalogoConStockAsync();
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            $"op-ef-{Guid.NewGuid():N}", e.Sesion);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoRegistrarCambio.EjecutarAsync(
            new DatosCambio(ok.VentaId, new[] { new LineaCambioEntrada(ficha.Lineas[0].Id, 1, true, null) }, Array.Empty<LineaCambioSalida>(),
                Array.Empty<PagoDiferenciaCambio>(), MediosPago.MercadoPago, "test", $"op-cambio-{Guid.NewGuid():N}", e.Sesion, null, null, null, false, null)));
        Assert.Contains("no tiene cobro de Mercado Pago suficiente", ex.Message);
    }

    [Fact]
    public async Task F06_reintegro_mp_con_cobertura_registra_y_reembolsa()
    {
        var (e, ventaId, _, precio) = await VentaMpConfirmadaAsync();
        using (e)
        {
            var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ventaId, e.Sesion))!;
            var resultado = await e.Servicios.CasoRegistrarCambio.EjecutarAsync(
                new DatosCambio(ventaId, new[] { new LineaCambioEntrada(ficha.Lineas[0].Id, 1, true, null) }, Array.Empty<LineaCambioSalida>(),
                    Array.Empty<PagoDiferenciaCambio>(), MediosPago.MercadoPago, "falla", $"op-cambio-{Guid.NewGuid():N}", e.Sesion, null, null, null, false, null));
            var registrado = Assert.IsType<ResultadoCambio.Registrado>(resultado);

            e.Fake.Responder("POST", "/v1/orders/ORD000001/refund", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
                """{"id":"ORD000001","status":"refunded","transactions":{"payments":[]}}"""));
            var pagoMp = ficha.Pagos.Single(p => p.Medio == MediosPago.MercadoPago);
            var reop = await e.Reembolsar.EjecutarAsync(ventaId, pagoMp.Id, precio, PermisosComerciales.ReintegroAutorizar, e.Sesion);
            await e.Reembolsar.MarcarConfirmadoLocalAsync(reop, e.Sesion);
            Assert.NotNull(registrado.CambioId);
        }
    }

    [Fact]
    public async Task Configuracion_solo_duena()
    {
        using var e = await EntornoMp.CrearAsync();
        var empId = await e.Servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("empleada", RolesLocal.Empleada), "clave-empleada-0000", e.Sesion);
        var sesionEmp = await e.Servicios.CasoIniciarSesion.EjecutarAsync("empleada", "clave-empleada-0000");
        _ = empId;
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Descubrir.EjecutarAsync(sesionEmp.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Configurar.ConectarAsync("X", sesionEmp.IdSesion));
    }
}
