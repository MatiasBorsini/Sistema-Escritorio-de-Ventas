using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// F09-T02 (caja): el reporte reutiliza F05 sin caja paralela; cuadra con
/// ventas, manuales, abonos, cambios y cierres.
/// </summary>
public sealed class ReportesCajaTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static (string Desde, string Hasta) Rango(DateOnly desde, DateOnly hasta)
    {
        var r = RangoFechas.PorDias(desde, hasta, Utc);
        return (r.DesdeUtc, r.HastaUtc);
    }

    [Fact]
    public async Task Historial_muestra_sesiones_del_rango_con_snapshots()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var sesionId = await e.AbrirCajaAsync(20000);
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-caja-venta", e.Sesion);
        await e.MovimientoCaja.EjecutarAsync("otro_ingreso", 5000, "Cambio", "op-caja-ing", e.Sesion);
        await e.MovimientoCaja.EjecutarAsync("gasto", 8000, "Limpieza", "op-caja-egr", e.Sesion);
        var arqueo = await e.CerrarCaja.PrevisualizarAsync(27000, e.Sesion);
        await e.CerrarCaja.EjecutarAsync(sesionId, 27000, arqueo.EfectivoEsperadoMinor, null, "op-caja-cierre", e.Sesion);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var sesiones = await e.Reportes.HistorialCajaAsync(d, h, 20, 0, e.Sesion);
        var sesion = Assert.Single(sesiones);
        Assert.Equal(20000, sesion.FondoInicialMinor);
        Assert.Equal(27000, sesion.EfectivoEsperadoMinor);
        Assert.Equal(27000, sesion.EfectivoContadoMinor);
        Assert.Equal(0, sesion.DiferenciaMinor);
        Assert.Equal("cerrada", sesion.Estado);

        // Rango sin sesiones: vacío, no error.
        var (d2, h2) = Rango(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
        Assert.Empty(await e.Reportes.HistorialCajaAsync(d2, h2, 20, 0, e.Sesion));
    }

    [Fact]
    public async Task Cierre_con_diferencia_conserva_snapshot()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var sesionId = await e.AbrirCajaAsync(20000);
        var (_, v1, _) = await e.CatalogoConStockAsync();
        await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-caja-dif", e.Sesion);
        var arqueo = await e.CerrarCaja.PrevisualizarAsync(29500, e.Sesion);
        Assert.Equal(30000, arqueo.EfectivoEsperadoMinor);
        await e.CerrarCaja.EjecutarAsync(sesionId, 29500, arqueo.EfectivoEsperadoMinor, "Faltan 500", "op-caja-cierre-dif", e.Sesion);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var sesion = Assert.Single(await e.Reportes.HistorialCajaAsync(d, h, 20, 0, e.Sesion));
        Assert.Equal(30000, sesion.EfectivoEsperadoMinor);
        Assert.Equal(29500, sesion.EfectivoContadoMinor);
        Assert.Equal(-500, sesion.DiferenciaMinor);
    }

    [Fact]
    public async Task Reporte_cuadra_con_ventas_manuales_abono_y_cambio()
    {
        using var e = await EntornoReportes.CrearAsync(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        await e.AbrirCajaAsync(20000);
        var cliente = await e.ClienteHabilitadoAsync(100000);
        var (_, v1, v2) = await e.CatalogoConStockAsync();

        // Venta 10000 en efectivo.
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v1, 1, 10000)], [new(MediosPago.Efectivo, 10000, 10000)], "op-cuadra-v", e.Sesion));
        // Abono 5000 por transferencia (deuda de otra venta fiada).
        await e.ConfirmarVenta.EjecutarAsync([new(v2, 1, 12000)], [], "op-cuadra-fiada", e.Sesion, cliente, null, true);
        var pagosAbono = new[] { new NuevoPago(MediosPago.Transferencia, 5000, null) };
        var preview = await e.Cuenta.PrevisualizarAbonoAsync(cliente, 5000, pagosAbono, e.Sesion);
        await e.RegistrarAbono.EjecutarAsync(new(cliente, 5000, pagosAbono, preview.Aplicaciones, preview.SaldoAnteriorMinor, "op-cuadra-abono", e.Sesion));
        // Ingreso manual 3000 + retiro dueña 1000 (requiere permiso: entorno lo concede).
        await e.MovimientoCaja.EjecutarAsync("otro_ingreso", 3000, "Ajuste", "op-cuadra-ing", e.Sesion);
        await e.MovimientoCaja.EjecutarAsync("retiro_propietario", 1000, "Retiro", "op-cuadra-ret", e.Sesion);
        // Cambio con cobro 2000 por débito sobre la primera venta.
        var ficha = (await e.Ventas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok.VentaId, [new(ficha.Lineas[0].Id, 1, true, null)], [new(v2, 1, 12000)],
            [new(MediosPago.Debito, 2000, null)], MediosPago.Efectivo, "Sube", "op-cuadra-cambio", e.Sesion), default);
        // Reintegro: devolución pura de otra venta de 12000.
        var ok2 = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.ConfirmarVenta.EjecutarAsync(
            [new(v2, 1, 12000)], [new(MediosPago.Efectivo, 12000, 12000)], "op-cuadra-v2", e.Sesion));
        var ficha2 = (await e.Ventas.ObtenerFichaAsync(ok2.VentaId, e.Sesion))!;
        await e.RegistrarCambio.EjecutarAsync(new DatosCambio(
            ok2.VentaId, [new(ficha2.Lineas[0].Id, 1, true, null)], [], [],
            MediosPago.Efectivo, "Devuelve", "op-cuadra-dev", e.Sesion), default);

        var (d, h) = Rango(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var medios = await e.Reportes.DesgloseMediosAsync(d, h, e.Sesion);
        // Efectivo: ventas 10000+12000, reintegros 12000 → neto 10000.
        Assert.Equal(22000, medios.First(m => m.Medio == MediosPago.Efectivo).VentasMinor);
        Assert.Equal(12000, medios.First(m => m.Medio == MediosPago.Efectivo).ReintegrosMinor);
        Assert.Equal(10000, medios.First(m => m.Medio == MediosPago.Efectivo).NetoMinor);
        // Transferencia: solo el abono 5000.
        Assert.Equal(5000, medios.First(m => m.Medio == MediosPago.Transferencia).AbonosMinor);
        // Débito: solo el cobro del cambio 2000.
        Assert.Equal(2000, medios.First(m => m.Medio == MediosPago.Debito).CambiosCobrosMinor);

        // Ventas 10000+12000+12000=34000; cobros 10000+5000+2000+12000=29000;
        // reintegros 12000; flujo neto 17000.
        var resumen = await e.Reportes.ResumenVentasAsync(d, h, e.Sesion);
        Assert.Equal(34000, resumen.BrutasMinor);
        Assert.Equal(29000, resumen.CobradoMinor);
        Assert.Equal(12000, resumen.ReintegrosMinor);
        Assert.Equal(17000, resumen.FlujoNetoMinor);
    }
}
