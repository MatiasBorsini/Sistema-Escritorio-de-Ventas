using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Caja;

public sealed record VentaCajaPrueba(string VentaId, string Numero, string PagoId);

public sealed class CajaTests
{
    [Fact]
    public async Task Apertura_guarda_fondo_usuario_operacion_y_no_crea_movimiento_de_apertura()
    {
        using var e = await Entorno.CrearAsync();
        var id = await e.AbrirAsync(1250000);
        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(id, actual.Sesion.Id);
        Assert.Equal("abierta", actual.Sesion.Estado);
        Assert.Equal(e.DuenaId, actual.Sesion.UsuarioAperturaId);
        Assert.Equal(1250000, actual.Sesion.FondoInicialMinor);
        Assert.Equal("ARS", actual.Sesion.Moneda);
        Assert.Equal(1250000, actual.EfectivoEsperadoMinor);
        Assert.Empty(actual.Movimientos);
        Assert.Equal(0, await e.ContarAsync("caja_movimiento"));
        Assert.Equal(1, await e.ContarAsync("caja_operacion"));
    }

    [Fact]
    public async Task Apertura_acepta_cero_bloquea_doble_y_no_permite_fondo_negativo()
    {
        using var e = await Entorno.CrearAsync();
        await e.Servicios.CasoAbrirCaja.EjecutarAsync(0, e.Op(), e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoAbrirCaja.EjecutarAsync(0, e.Op(), e.Sesion));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => e.Servicios.CasoAbrirCaja.EjecutarAsync(-1, e.Op(), e.Sesion));
        Assert.Equal(1, await e.ContarAsync("caja_sesion"));
    }

    [Fact]
    public async Task Permisos_de_caja_se_resuelven_por_rol_y_denegacion_individual()
    {
        using var e = await Entorno.CrearAsync();
        var sesionEmpleado = await e.CrearEmpleadaAsync();
        var permisos = await e.Servicios.Autorizador.ObtenerPermisosEfectivosAsync(sesionEmpleado, PermisosCaja.Todos);
        Assert.Contains(PermisosCaja.Abrir, permisos);
        Assert.Contains(PermisosCaja.Consultar, permisos);
        Assert.Contains(PermisosCaja.Movimiento, permisos);
        Assert.Contains(PermisosCaja.Cerrar, permisos);
        Assert.DoesNotContain(PermisosCaja.RetiroPropietario, permisos);
        Assert.DoesNotContain(PermisosCaja.CorregirMedio, permisos);

        var vm = new CajaViewModel(e.Servicios, () => sesionEmpleado);
        await vm.CargarAsyncPublic();
        Assert.True(vm.PuedeAbrir);
        Assert.True(vm.PuedeConsultar);
        Assert.False(vm.PuedeRetirar);
        Assert.False(vm.PuedeCorregir);
        Assert.DoesNotContain(vm.CategoriasDisponibles, x => x.Valor == CategoriasMovimientoCaja.RetiroPropietario);

        await e.Servicios.CasoConfigurarPermiso.EjecutarAsync(e.EmpleadaId!, PermisosCaja.Abrir, false, e.Sesion);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicios.CasoAbrirCaja.EjecutarAsync(0, e.Op(), sesionEmpleado));
    }

    [Fact]
    public async Task Ingresos_egresos_y_retiro_actualizan_solo_efectivo_con_categoria_y_motivo()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(10000);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.CambioFondo, 1000, "Cambio para caja", e.Op(), e.Sesion);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.OtroIngreso, 2000, "Reintegro de gasto", e.Op(), e.Sesion);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.Gasto, 300, "Bolsas", e.Op(), e.Sesion);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.RetiroPropietario, 100, "Retiro autorizado", e.Op(), e.Sesion);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.OtroEgreso, 50, "Otro gasto", e.Op(), e.Sesion);

        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(12550, actual.EfectivoEsperadoMinor);
        Assert.Equal(5, actual.Movimientos.Count);
        Assert.Contains(actual.Movimientos, x => x.Tipo == TiposMovimientoCaja.Ingreso && x.Categoria == CategoriasMovimientoCaja.CambioFondo && x.ImporteMinor == 1000);
        Assert.Contains(actual.Movimientos, x => x.Tipo == TiposMovimientoCaja.Egreso && x.Categoria == CategoriasMovimientoCaja.RetiroPropietario && x.ImporteMinor == -100);
        Assert.All(actual.Movimientos, x => Assert.Equal(e.DuenaNombre, x.UsuarioNombre));
    }

    [Fact]
    public async Task Movimiento_manual_rechaza_importe_motivo_y_sesion_cerrada()
    {
        using var e = await Entorno.CrearAsync();
        var caja = await e.AbrirAsync(0);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.Gasto, 0, "Motivo", e.Op(), e.Sesion));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.Gasto, 100, " ", e.Op(), e.Sesion));
        await e.CerrarAsync(0, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.Gasto, 100, "Después del cierre", e.Op(), e.Sesion));
        var detalle = (await e.Servicios.ConsultasCaja.ObtenerDetalleAsync(caja, e.Sesion))!;
        Assert.Equal("cerrada", detalle.Sesion.Estado);
        Assert.Empty(detalle.Movimientos);
    }

    [Fact]
    public async Task Empleada_no_puede_registrar_retiro_del_propietario()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var empleada = await e.CrearEmpleadaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.RetiroPropietario, 100, "Prueba", e.Op(), empleada));
        Assert.Empty((await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Movimientos);
    }

    [Fact]
    public async Task Venta_sin_caja_abierta_falla_sin_venta_pago_ni_consumo_de_stock()
    {
        using var e = await Entorno.CrearAsync();
        var variante = await e.CrearArticuloAsync(1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]));
        Assert.Equal("No hay una caja abierta. Abrí la caja antes de registrar una venta.", ex.Message);
        Assert.Equal(0, await e.ContarAsync("venta"));
        Assert.Equal(0, await e.ContarAsync("pago"));
        Assert.Equal(0, await e.ContarAsync("movimiento_inventario", "tipo = 'egreso'"));
        Assert.Equal(0, await e.ContarAsync("caja_movimiento"));
        Assert.Equal(1, await e.StockDisponibleAsync(variante));
    }

    [Fact]
    public async Task Todos_los_medios_se_concilian_y_solo_efectivo_modifica_el_esperado()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(10);
        foreach (var medio in MediosPago.Todos)
        {
            var pago = new NuevoPago(medio, 10000, medio == MediosPago.Efectivo ? 10000 : null);
            await e.VenderAsync(variante, [pago]);
        }
        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(10000, actual.EfectivoEsperadoMinor);
        Assert.Equal(5, actual.Movimientos.Count);
        Assert.Equal(10000, actual.ResumenMedios.Single(x => x.Medio == MediosCaja.Efectivo).ImporteMinor);
        foreach (var medio in MediosCajaNoEfectivo())
            Assert.Equal(10000, actual.ResumenMedios.Single(x => x.Medio == medio).ImporteMinor);
    }

    [Fact]
    public async Task Venta_combinada_crea_un_movimiento_por_pago_y_efecto_por_medio()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(5000);
        var variante = await e.CrearArticuloAsync(2);
        var venta = await e.VenderAsync(variante,
        [
            new NuevoPago(MediosPago.Efectivo, 10000, 10000),
            new NuevoPago(MediosPago.Debito, 10000, null),
        ], cantidad: 2);
        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(15000, actual.EfectivoEsperadoMinor);
        Assert.Equal(2, actual.Movimientos.Count);
        Assert.All(actual.Movimientos, x => Assert.Equal(venta.VentaId, x.VentaId));
        Assert.Contains(actual.Movimientos, x => x.Medio == MediosCaja.Efectivo && x.ImporteMinor == 10000 && x.PagoId is not null);
        Assert.Contains(actual.Movimientos, x => x.Medio == MediosCaja.Debito && x.ImporteMinor == 10000 && x.PagoId is not null);
    }

    [Fact]
    public async Task Falla_de_persistencia_de_caja_revierte_venta_stock_pago_y_auditoria()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(1);
        await e.EjecutarSqlAsync("CREATE TRIGGER test_falla_movimiento_caja BEFORE INSERT ON caja_movimiento BEGIN SELECT RAISE(ABORT, 'fallo de prueba de caja'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)], "op-fallo-caja"));
        Assert.Equal(0, await e.ContarAsync("venta", "operacion_id = 'op-fallo-caja'"));
        Assert.Equal(0, await e.ContarAsync("pago"));
        Assert.Equal(0, await e.ContarAsync("movimiento_inventario", "tipo = 'egreso'"));
        Assert.Equal(0, await e.ContarAsync("caja_operacion", "operacion_id = 'op-fallo-caja'"));
        Assert.Equal(1, await e.StockDisponibleAsync(variante));
        Assert.Equal(0, await e.ContarAsync("auditoria", "operacion_id = 'op-fallo-caja'"));
    }

    [Fact]
    public async Task Anulacion_combinada_compensa_medios_y_devuelve_efectivo_esperado_al_fondo()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(5000);
        var variante = await e.CrearArticuloAsync(2);
        var venta = await e.VenderAsync(variante,
        [
            new NuevoPago(MediosPago.Efectivo, 10000, 10000),
            new NuevoPago(MediosPago.Debito, 10000, null),
        ], cantidad: 2);
        await e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Venta de prueba", e.Op(), e.Sesion);
        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(5000, actual.EfectivoEsperadoMinor);
        Assert.Equal(4, actual.Movimientos.Count);
        Assert.Equal(0, actual.ResumenMedios.Single(x => x.Medio == MediosCaja.Efectivo).ImporteMinor);
        Assert.Equal(0, actual.ResumenMedios.Single(x => x.Medio == MediosCaja.Debito).ImporteMinor);
        Assert.Equal(2, actual.Movimientos.Count(x => x.Tipo == TiposMovimientoCaja.Reintegro));
        Assert.Equal(2, await e.StockDisponibleAsync(variante));
    }

    [Fact]
    public async Task Anulacion_de_venta_de_sesion_cerrada_se_registra_en_la_sesion_actual()
    {
        using var e = await Entorno.CrearAsync();
        var cajaOriginal = await e.AbrirAsync(10000);
        var variante = await e.CrearArticuloAsync(1);
        var venta = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
        await e.CerrarAsync(20000, null);
        var nueva = await e.AbrirAsync(20000);
        await e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Anulación posterior", e.Op(), e.Sesion);

        var historico = (await e.Servicios.ConsultasCaja.ObtenerDetalleAsync(cajaOriginal, e.Sesion))!;
        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal("cerrada", historico.Sesion.Estado);
        Assert.Equal(20000, historico.Sesion.EfectivoEsperadoMinor);
        Assert.Single(historico.Movimientos);
        Assert.Equal(nueva, actual.Sesion.Id);
        Assert.Equal(10000, actual.EfectivoEsperadoMinor);
        Assert.Single(actual.Movimientos);
        Assert.Equal(TiposMovimientoCaja.Reintegro, actual.Movimientos[0].Tipo);
        Assert.Equal(-10000, actual.Movimientos[0].ImporteMinor);
    }

    [Fact]
    public async Task Anulacion_financiera_sin_caja_abierta_no_anula_ni_restituye_stock()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(2);
        var venta = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
        await e.CerrarAsync(10000, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Sin sesión", e.Op(), e.Sesion));
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!;
        Assert.Equal("confirmada", ficha.Venta.Estado);
        Assert.Equal(1, await e.StockDisponibleAsync(variante));
    }

    [Fact]
    public async Task Error_de_caja_durante_anulacion_revierte_reversion_estado_y_stock()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(1);
        var venta = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
        await e.EjecutarSqlAsync("CREATE TRIGGER test_falla_reintegro BEFORE INSERT ON caja_movimiento WHEN NEW.tipo='reintegro' BEGIN SELECT RAISE(ABORT, 'fallo de prueba de reintegro'); END;");

        await Assert.ThrowsAsync<SqliteException>(() => e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId, "Reversión de prueba", e.Op(), e.Sesion));
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!;
        Assert.Equal("confirmada", ficha.Venta.Estado);
        Assert.Null(ficha.Anulacion);
        Assert.Equal(0, await e.ContarAsync("reversion_venta"));
        Assert.Equal(0, await e.StockDisponibleAsync(variante));
        Assert.Equal(1, await e.ContarAsync("caja_movimiento", "tipo = 'venta'"));
        Assert.Equal(0, await e.ContarAsync("caja_movimiento", "tipo = 'reintegro'"));
    }

    [Fact]
    public async Task Correccion_compensa_en_sesion_actual_y_pago_original_permanece_inmutable()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(20000);
        var variante = await e.CrearArticuloAsync(2);
        var efectivo = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
        var debito = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Debito, 10000, null)]);
        await e.Servicios.CasoCorregirMedioPago.EjecutarAsync(efectivo.PagoId, MediosPago.Debito, "Se cobró con débito", e.Op(), e.Sesion);
        await e.Servicios.CasoCorregirMedioPago.EjecutarAsync(debito.PagoId, MediosPago.Efectivo, "Se cobró en efectivo", e.Op(), e.Sesion);

        var actual = (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!;
        Assert.Equal(30000, actual.EfectivoEsperadoMinor);
        Assert.Equal(4, actual.Movimientos.Count(x => x.Tipo == TiposMovimientoCaja.Correccion));
        Assert.Equal(10000, actual.ResumenMedios.Single(x => x.Medio == MediosCaja.Efectivo).ImporteMinor);
        Assert.Equal(10000, actual.ResumenMedios.Single(x => x.Medio == MediosCaja.Debito).ImporteMinor);
        Assert.Equal("efectivo", await e.TextoAsync("SELECT medio FROM pago WHERE id = $id", ("$id", efectivo.PagoId)));
        Assert.Equal("debito", await e.TextoAsync("SELECT medio FROM pago WHERE id = $id", ("$id", debito.PagoId)));
        Assert.Empty(await e.Servicios.ConsultasCaja.BuscarPagosCorregiblesAsync(efectivo.Numero, e.Sesion));
    }

    [Fact]
    public async Task Correccion_es_solo_duena_exige_motivo_y_una_por_pago()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(1);
        var venta = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
        var empleada = await e.CrearEmpleadaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicios.CasoCorregirMedioPago.EjecutarAsync(venta.PagoId, MediosPago.Debito, "No", e.Op(), empleada));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Servicios.CasoCorregirMedioPago.EjecutarAsync(venta.PagoId, MediosPago.Debito, " ", e.Op(), e.Sesion));
        await e.Servicios.CasoCorregirMedioPago.EjecutarAsync(venta.PagoId, MediosPago.Debito, "Corrección", e.Op(), e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoCorregirMedioPago.EjecutarAsync(venta.PagoId, MediosPago.Credito, "Segunda", e.Op(), e.Sesion));
        Assert.Equal(1, await e.ContarAsync("pago_correccion"));
    }

    [Fact]
    public async Task Correccion_de_medio_sin_sesion_abierta_es_rechazada()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(0);
        var variante = await e.CrearArticuloAsync(1);
        var venta = await e.VenderAsync(variante, [new NuevoPago(MediosPago.Debito, 10000, null)]);
        await e.CerrarAsync(0, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoCorregirMedioPago.EjecutarAsync(venta.PagoId, MediosPago.Efectivo, "No hay sesión", e.Op(), e.Sesion));
        Assert.Equal(0, await e.ContarAsync("pago_correccion"));
    }

    [Fact]
    public async Task CajaViewModel_abre_sesion_y_registra_movimiento_con_datos_de_presentacion()
    {
        using var e = await Entorno.CrearAsync();
        var vm = new CajaViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarAsyncPublic();
        Assert.True(vm.PuedeVer);
        Assert.True(vm.CajaCerrada);
        vm.FondoInicialTexto = "123,45";
        await vm.AbrirCajaAsyncPublic();
        Assert.True(vm.CajaAbierta);
        Assert.Equal("ARS 123,45", vm.EfectivoEsperadoTexto);
        Assert.Contains(vm.CategoriasDisponibles, x => x.Texto == "Agregar cambio");

        vm.TipoMovimientoSeleccionado = "Egreso";
        vm.CategoriaSeleccionada = vm.CategoriasDisponibles.Single(x => x.Valor == CategoriasMovimientoCaja.Gasto);
        vm.ImporteMovimientoTexto = "3,45";
        vm.MotivoMovimiento = "Bolsas";
        await vm.RegistrarMovimientoAsyncPublic();
        Assert.Equal("ARS 120,00", vm.EfectivoEsperadoTexto);
        Assert.Single(vm.Movimientos);
        Assert.Contains("Gasto · Bolsas", vm.Movimientos[0].Descripcion);
    }

    [Fact]
    public async Task Arqueo_ciego_no_expone_esperado_antes_de_contado_y_revela_luego()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(10000);
        var cerrado = false;
        var vm = new ArqueoCiegoViewModel(e.Servicios, () => e.Sesion,
            (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.Sesion.Id,
            e.DuenaNombre, "2026-10-03T09:00:00Z", () => Task.CompletedTask, () => { cerrado = true; return Task.CompletedTask; });
        Assert.Null(vm.Resultado);
        Assert.False(vm.HayResultado);
        Assert.Equal(string.Empty, vm.EfectivoEsperadoTexto);
        vm.EfectivoContadoTexto = "90,00";
        await vm.RevelarAsyncPublic();
        Assert.Equal(10000, vm.Resultado!.EfectivoEsperadoMinor);
        Assert.Equal(9000, vm.Resultado.EfectivoContadoMinor);
        Assert.Equal(-1000, vm.Resultado.DiferenciaMinor);
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("observación", vm.Error, StringComparison.OrdinalIgnoreCase);
        vm.Observacion = "Faltante contado";
        await vm.ConfirmarAsyncPublic();
        Assert.True(cerrado);
        var actual = (await e.Servicios.ConsultasCaja.BuscarHistorialAsync(5, 0, e.Sesion)).Single();
        Assert.Equal(10000, actual.EfectivoEsperadoMinor);
        Assert.Equal(9000, actual.EfectivoContadoMinor);
        Assert.Equal(-1000, actual.DiferenciaMinor);
        Assert.Equal("Faltante contado", actual.ObservacionCierre);
        Assert.Equal(0, await e.ContarAsync("caja_movimiento"));
    }

    [Fact]
    public async Task Cierre_exacto_acepta_sin_observacion_y_no_admite_segundo_cierre_o_movimientos()
    {
        using var e = await Entorno.CrearAsync();
        await e.AbrirAsync(3500);
        var vm = new CajaViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarAsyncPublic();
        Assert.Equal("ARS 35,00", vm.EfectivoEsperadoTexto);
        var resultado = await e.CerrarAsync(3500, null);
        Assert.Equal(0, resultado.DiferenciaMinor);
        Assert.Equal(e.DuenaId, (await e.Servicios.ConsultasCaja.BuscarHistorialAsync(5, 0, e.Sesion)).Single().UsuarioCierreId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.CerrarAsync(3500, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.Gasto, 10, "Tarde", e.Op(), e.Sesion));
        var nueva = await e.AbrirAsync(0);
        Assert.NotEqual(resultado.SesionId, nueva);
        Assert.Equal(0, (await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion))!.EfectivoEsperadoMinor);
    }

    [Fact]
    public async Task Sobrante_se_registra_sin_movimiento_automatico_y_el_historico_no_se_edita()
    {
        using var e = await Entorno.CrearAsync();
        var caja = await e.AbrirAsync(10000);
        var arqueo = await e.Servicios.CasoCerrarCaja.PrevisualizarAsync(11000, e.Sesion);
        Assert.Equal(10000, arqueo.EfectivoEsperadoMinor);
        Assert.Equal(11000, arqueo.EfectivoContadoMinor);
        Assert.Equal(1000, arqueo.DiferenciaMinor);
        await Assert.ThrowsAsync<ArgumentException>(() => e.Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId, 11000, 10000, null, e.Op(), e.Sesion));
        await e.Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId, 11000, 10000, "Sobrante contado", e.Op(), e.Sesion);
        var detalle = (await e.Servicios.ConsultasCaja.ObtenerDetalleAsync(caja, e.Sesion))!;
        Assert.Equal(1000, detalle.Sesion.DiferenciaMinor);
        Assert.Empty(detalle.Movimientos);
        await Assert.ThrowsAsync<SqliteException>(() => e.EjecutarSqlAsync("UPDATE caja_sesion SET fondo_inicial_minor = 1 WHERE id = '" + caja + "';"));
    }

    [Fact]
    public async Task Venta_antes_del_cierre_se_incluye_y_cierre_primero_impide_vender()
    {
        using (var primeroVenta = await Entorno.CrearAsync())
        {
            await primeroVenta.AbrirAsync(10000);
            var variante = await primeroVenta.CrearArticuloAsync(1);
            await primeroVenta.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]);
            var cierre = await primeroVenta.CerrarAsync(20000, null);
            Assert.Equal(20000, cierre.EfectivoEsperadoMinor);
            Assert.Equal(1, await primeroVenta.ContarAsync("caja_movimiento", "tipo='venta'"));
        }

        using (var primeroCierre = await Entorno.CrearAsync())
        {
            await primeroCierre.AbrirAsync(0);
            var variante = await primeroCierre.CrearArticuloAsync(1);
            await primeroCierre.CerrarAsync(0, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => primeroCierre.VenderAsync(variante, [new NuevoPago(MediosPago.Efectivo, 10000, 10000)]));
            Assert.Equal(0, await primeroCierre.ContarAsync("venta"));
            Assert.Equal(1, await primeroCierre.StockDisponibleAsync(variante));
        }
    }

    [Fact]
    public async Task Consulta_historial_devuelve_cierres_con_snapshot_y_movimientos()
    {
        using var e = await Entorno.CrearAsync();
        var id = await e.AbrirAsync(5000);
        await e.Servicios.CasoRegistrarMovimientoCaja.EjecutarAsync(CategoriasMovimientoCaja.OtroIngreso, 200, "Cambio", e.Op(), e.Sesion);
        await e.CerrarAsync(5100, "Sobrante contado");
        Assert.Null(await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion));
        var pagina = await e.Servicios.ConsultasCaja.BuscarHistorialAsync(1, 0, e.Sesion);
        Assert.Single(pagina);
        var detalle = (await e.Servicios.ConsultasCaja.ObtenerDetalleAsync(id, e.Sesion))!;
        Assert.Equal(5200, detalle.Sesion.EfectivoEsperadoMinor);
        Assert.Equal(5100, detalle.Sesion.EfectivoContadoMinor);
        Assert.Equal(-100, detalle.Sesion.DiferenciaMinor);
        Assert.Equal("Sobrante contado", detalle.Sesion.ObservacionCierre);
        Assert.Single(detalle.Movimientos);
    }

    [Fact]
    public async Task V10_a_V11_vacio_se_aplica_y_marca_esquema_11()
    {
        var (dir, ruta) = CrearBaseTemporal();
        try
        {
            var v10 = MigracionesF01.Todas.Take(10).ToArray();
            Assert.Equal(Enumerable.Range(1, 10), await new MigradorBaseDatos(ruta).MigrarAsync(v10));
            var respaldo = Path.Combine(dir, "respaldos");
            Assert.Equal(new[] { 11 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas.Take(11).ToArray(), respaldo));
            await using var db = await AbrirAsync(ruta);
            Assert.Equal(11L, await ScalarLongAsync(db, "SELECT MAX(version) FROM schema_migrations"));
            Assert.Equal(0L, await ScalarLongAsync(db, "SELECT COUNT(*) FROM venta"));
            Assert.Equal(0L, await ScalarLongAsync(db, "SELECT COUNT(*) FROM caja_movimiento"));
            Assert.Equal("ok", await ScalarTextAsync(db, "PRAGMA integrity_check"));
            Assert.Single(Directory.GetFiles(respaldo, "*.db"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task V10_a_V11_con_venta_y_pago_preserva_historicos_sin_backfill_de_caja_y_respalda()
    {
        var (dir, ruta) = CrearBaseTemporal();
        try
        {
            var v10 = MigracionesF01.Todas.Take(10).ToArray();
            await new MigradorBaseDatos(ruta).MigrarAsync(v10);
            await using (var db = await AbrirAsync(ruta))
            {
                await ExecAsync(db, "INSERT INTO usuario (id,nombre,rol,activo,creado_en) VALUES ('u-mig','Dueña','duena',1,'2026-10-03T09:00:00Z');");
                await ExecAsync(db, "INSERT INTO venta (id,estado,total_minor,moneda,creada_en,confirmada_en,numero,operacion_id,creado_por_usuario_id) VALUES ('v-mig','confirmada',12345,'ARS','2026-10-03T09:00:00Z','2026-10-03T09:00:00Z','T-000001','op-v10','u-mig');");
                await ExecAsync(db, "INSERT INTO pago (id,venta_id,medio,importe_minor,creado_en,recibido_minor) VALUES ('p-mig','v-mig','efectivo',12345,'2026-10-03T09:00:00Z',13000);");
            }
            var respaldos = Path.Combine(dir, "respaldos");
            Assert.Equal(new[] { 11 }, await new MigradorBaseDatos(ruta).MigrarAsync(MigracionesF01.Todas.Take(11).ToArray(), respaldos));
            await using var migrada = await AbrirAsync(ruta);
            Assert.Equal(1L, await ScalarLongAsync(migrada, "SELECT COUNT(*) FROM venta WHERE id='v-mig' AND total_minor=12345 AND numero='T-000001' AND estado='confirmada'"));
            Assert.Equal(1L, await ScalarLongAsync(migrada, "SELECT COUNT(*) FROM pago WHERE id='p-mig' AND medio='efectivo' AND importe_minor=12345 AND recibido_minor=13000"));
            Assert.Equal(0L, await ScalarLongAsync(migrada, "SELECT COUNT(*) FROM caja_movimiento"));
            Assert.Equal(0L, await ScalarLongAsync(migrada, "SELECT COUNT(*) FROM caja_operacion"));
            Assert.Equal(1L, await ScalarLongAsync(migrada, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ux_caja_una_sesion_abierta'"));
            Assert.Equal("ok", await ScalarTextAsync(migrada, "PRAGMA integrity_check"));
            var backup = Assert.Single(Directory.GetFiles(respaldos, "*.db"));
            await using var backupDb = await AbrirAsync(backup, readOnly: true);
            Assert.Equal(1L, await ScalarLongAsync(backupDb, "SELECT COUNT(*) FROM pago WHERE id='p-mig'"));
            Assert.Equal("ok", await ScalarTextAsync(backupDb, "PRAGMA integrity_check"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static IReadOnlyList<string> MediosCajaNoEfectivo() => [MediosCaja.Transferencia, MediosCaja.Debito, MediosCaja.Credito, MediosCaja.Billetera];
    private static (string Directory, string Database) CrearBaseTemporal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return (dir, Path.Combine(dir, "caja-f05.db"));
    }

    private static async Task<SqliteConnection> AbrirAsync(string ruta, bool readOnly = false)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ruta, Pooling = false, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite }.ToString());
        await db.OpenAsync();
        return db;
    }
    private static async Task ExecAsync(SqliteConnection db, string sql)
    { using var cmd = db.CreateCommand(); cmd.CommandText = sql; await cmd.ExecuteNonQueryAsync(); }
    private static async Task<long> ScalarLongAsync(SqliteConnection db, string sql)
    { using var cmd = db.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(await cmd.ExecuteScalarAsync()); }
    private static async Task<string> ScalarTextAsync(SqliteConnection db, string sql)
    { using var cmd = db.CreateCommand(); cmd.CommandText = sql; return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? string.Empty; }

    private sealed class Entorno : IDisposable
    {
        private readonly string _directorio;
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string DuenaId { get; }
        public string DuenaNombre => "Dueña";
        public string? EmpleadaId { get; private set; }
        public string Ruta { get; }

        private Entorno(string directorio, string ruta, ServiciosComerciales servicios, string sesion, string duenaId)
            => (_directorio, Ruta, Servicios, Sesion, DuenaId) = (directorio, ruta, servicios, sesion, duenaId);

        public static async Task<Entorno> CrearAsync()
        {
            var (dir, ruta) = CrearBaseTemporal();
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("Dueña", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("Dueña", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCatalogo.ProductosGestionar, PermisosCatalogo.VariantesGestionar,
                PermisosCatalogo.PreciosGestionar, PermisosInventario.CargaInicialEstablecer,
                PermisosVentas.VentasConfirmar, PermisosVentas.VentasAnular,
            })
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            return new Entorno(dir, ruta, servicios, sesion.IdSesion, duenaId);
        }

        public string Op() => "op-caja-" + Guid.NewGuid().ToString("N");
        public Task<string> AbrirAsync(long fondo) => Servicios.CasoAbrirCaja.EjecutarAsync(fondo, Op(), Sesion);
        public async Task<ResultadoCierreCaja> CerrarAsync(long contado, string? observacion)
        {
            var arqueo = await Servicios.CasoCerrarCaja.PrevisualizarAsync(contado, Sesion);
            return await Servicios.CasoCerrarCaja.EjecutarAsync(arqueo.SesionId, contado, arqueo.EfectivoEsperadoMinor, observacion, Op(), Sesion);
        }
        public async Task<string> CrearEmpleadaAsync()
        {
            EmpleadaId = await Servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("Empleada", RolesLocal.Empleada), "clave-empleada-000000", Sesion);
            return (await Servicios.CasoIniciarSesion.EjecutarAsync("Empleada", "clave-empleada-000000")).IdSesion;
        }
        public async Task<string> CrearArticuloAsync(long stock)
        {
            var producto = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Prenda caja", null, "cat-rem"), Sesion);
            var variante = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(producto, "M", "Azul"), Sesion);
            await Servicios.CasoFijarPrecio.EjecutarAsync(variante, 10000, Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync([new FilaCargaInicial(variante, stock)], Op(), Sesion);
            return variante;
        }
        public async Task<VentaCajaPrueba> VenderAsync(string variante, IReadOnlyList<NuevoPago> pagos, string? operacion = null, long cantidad = 1)
        {
            var ok = await Servicios.CasoConfirmarVenta.EjecutarAsync([new NuevaLineaVenta(variante, cantidad, 10000)], pagos, operacion ?? Op(), Sesion);
            var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(ok);
            var ficha = (await Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, Sesion))!;
            return new(venta.VentaId, venta.Numero, ficha.Pagos[0].Id);
        }
        public async Task<long> StockDisponibleAsync(string variante)
        {
            var codigo = (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(variante, Sesion))!.CodigoInterno;
            return (await Servicios.ConsultasInventario.ObtenerClasificadaAsync(codigo, Sesion))!.Disponible;
        }
        public async Task<long> ContarAsync(string tabla, string? where = null)
        {
            var sql = $"SELECT COUNT(*) FROM {tabla}" + (string.IsNullOrWhiteSpace(where) ? string.Empty : $" WHERE {where}");
            await using var db = await CajaTests.AbrirAsync(Ruta, readOnly: true);
            return await ScalarLongAsync(db, sql);
        }
        public async Task<string> TextoAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var db = await CajaTests.AbrirAsync(Ruta, readOnly: true);
            using var cmd = db.CreateCommand(); cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
            return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? string.Empty;
        }
        public async Task EjecutarSqlAsync(string sql)
        {
            await using var db = await CajaTests.AbrirAsync(Ruta);
            await ExecAsync(db, sql);
        }
        public void Dispose() { if (Directory.Exists(_directorio)) Directory.Delete(_directorio, true); }
    }
}
