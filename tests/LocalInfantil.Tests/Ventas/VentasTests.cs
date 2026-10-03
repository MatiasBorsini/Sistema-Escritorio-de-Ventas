using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Ventas;

/// <summary>
/// F04: confirmación atómica, pagos, idempotencia, snapshots históricos y
/// anulación total con restitución. Base temporal aislada por test.
/// </summary>
public sealed class VentasTests
{
    [Fact]
    public async Task Venta_correcta_consume_disponible_y_registra_todo()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (prod, v1, v2) = await e.CatalogoConStockAsync();
        var precio1 = await e.PrecioDeAsync(v1);
        var precio2 = await e.PrecioDeAsync(v2);
        var total = 2 * precio1 + 1 * precio2;

        var resultado = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 2, precio1), new NuevaLineaVenta(v2, 1, precio2) },
            new[] { new NuevoPago(MediosPago.Efectivo, total, total) },
            "op-venta-001",
            e.Sesion);
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(resultado);
        Assert.Equal("T-000001", ok.Numero);
        Assert.Equal(total, ok.TotalMinor);

        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal("confirmada", ficha.Venta.Estado);
        Assert.Equal(total, ficha.Venta.TotalMinor);
        Assert.Equal(2, ficha.Lineas.Count);
        Assert.All(ficha.Lineas, l => Assert.False(string.IsNullOrWhiteSpace(l.CodigoInterno)));
        Assert.All(ficha.Lineas, l => Assert.False(string.IsNullOrWhiteSpace(l.NombreProducto)));
        Assert.Single(ficha.Pagos);
        Assert.Equal(total, ficha.Pagos[0].ImporteMinor);

        // Stock: Disponible disminuye; físico = disponible + apartado.
        var ex1 = (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!;
        Assert.Equal(8, ex1.Disponible);
        Assert.Equal(8, ex1.Fisico);
        Assert.Equal(0, ex1.Apartado);
        Assert.NotEmpty(await e.AuditoriaDeAsync(ok.VentaId));
        _ = prod;
    }

    [Fact]
    public async Task Sin_precio_vigente_se_rechaza_con_mensaje_humano()
    {
        using var e = await EntornoVentas.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Sin precio", null, "cat-otr"), e.Sesion);
        var v = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.Sesion);
        await e.Servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(v, 5) }, "op-carga-sp", e.Sesion);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v, 1, 10000) },
                new[] { new NuevoPago(MediosPago.Efectivo, 10000, 10000) },
                "op-venta-sp",
                e.Sesion));
        Assert.Equal("Esta variante no tiene un precio vigente.", error.Message);
    }

    [Fact]
    public async Task Stock_insuficiente_revierte_todo_sin_parciales()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 999, precio) },
                new[] { new NuevoPago(MediosPago.Efectivo, 999 * precio, 999 * precio) },
                "op-venta-falla",
                e.Sesion));
        Assert.Contains("Solo hay 10 unidades disponibles", error.Message);

        // Nada persiste: ni venta, ni movimientos, ni siquiera el número.
        Assert.Empty(await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion));
        Assert.Equal(10, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!.Disponible);
        var siguiente = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            "op-venta-ok",
            e.Sesion);
        Assert.Equal("T-000001", ((ResultadoConfirmacionVenta.Confirmada)siguiente).Numero);
    }

    [Fact]
    public async Task Ultimo_articulo_disponible_se_puede_vender()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);

        var resultado = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 10, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, 10 * precio, 10 * precio) },
            "op-venta-ult",
            e.Sesion);
        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(resultado);
        Assert.Equal(0, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!.Disponible);
    }

    [Fact]
    public async Task Precio_cambiado_aborta_y_obliga_revision()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var viejo = await e.PrecioDeAsync(v1);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v1, viejo + 1000, e.Sesion);
        var nuevo = viejo + 1000;

        var resultado = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, viejo) },
            new[] { new NuevoPago(MediosPago.Efectivo, viejo, viejo) },
            "op-venta-pc",
            e.Sesion);
        var cambio = Assert.IsType<ResultadoConfirmacionVenta.PrecioCambiado>(resultado);
        var detalle = Assert.Single(cambio.Cambios);
        Assert.Equal(viejo, detalle.PrecioEsperadoMinor);
        Assert.Equal(nuevo, detalle.PrecioVigenteMinor);

        // Aborto limpio: nada persiste.
        Assert.Empty(await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion));

        // Reconfirmando con el precio nuevo funciona y ese queda en snapshot.
        var reintento = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, nuevo) },
            new[] { new NuevoPago(MediosPago.Efectivo, nuevo, nuevo) },
            "op-venta-pc2",
            e.Sesion);
        var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(reintento);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal(nuevo, ficha.Lineas[0].PrecioUnitarioMinor);
    }

    [Fact]
    public async Task Historico_inmutable_ante_cambios_posteriores()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (prod, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            "op-venta-hist",
            e.Sesion);

        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v1, precio + 5000, e.Sesion);
        await e.Servicios.CasoEditarProducto.EjecutarAsync(prod, new EdicionProducto("Nombre Nuevo", null, "cat-buz"), e.Sesion);
        await e.Servicios.CasoEditarVariante.EjecutarAsync(v1, new EdicionVariante("XL", "Verde"), e.Sesion);

        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal(precio, ficha.Lineas[0].PrecioUnitarioMinor);
        Assert.Equal("Remera F04", ficha.Lineas[0].NombreProducto);
        Assert.Equal("M", ficha.Lineas[0].Talle);
    }

    [Fact]
    public async Task Pagos_cubren_todos_los_medios_y_combinados()
    {
        foreach (var medio in MediosPago.Todos)
        {
            using var e = await EntornoVentas.CrearAsync();
            var (_, v1, _) = await e.CatalogoConStockAsync();
            var precio = await e.PrecioDeAsync(v1);
            var recibido = medio == MediosPago.Efectivo ? precio : (long?)null;
            var resultado = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago(medio, precio, recibido) },
                $"op-venta-{medio}",
                e.Sesion);
            var ok = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(resultado);
            var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
            Assert.Equal(medio, ficha.Pagos[0].Medio);
        }

        // Combinado débito + efectivo con vuelto.
        using var entorno = await EntornoVentas.CrearAsync();
        var (_, v, _) = await entorno.CatalogoConStockAsync();
        var p = await entorno.PrecioDeAsync(v);
        var total = 2 * p;
        var combinado = await entorno.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v, 2, p) },
            new[] { new NuevoPago(MediosPago.Debito, p, null), new NuevoPago(MediosPago.Efectivo, p, p + 5000) },
            "op-venta-comb",
            entorno.Sesion);
        var okComb = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(combinado);
        var fichaComb = (await entorno.Servicios.ConsultasVentas.ObtenerFichaAsync(okComb.VentaId, entorno.Sesion))!;
        Assert.Equal(2, fichaComb.Pagos.Count);
        Assert.Equal(5000, fichaComb.Pagos.First(x => x.Medio == MediosPago.Efectivo).VueltoMinor);
    }

    [Fact]
    public async Task Pagos_invalidos_se_rechazan_con_mensaje_humano()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);

        // Suma menor.
        var menor = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago(MediosPago.Efectivo, precio - 1, precio - 1) },
                "op-pago-menor",
                e.Sesion));
        Assert.Equal("El importe de los pagos no alcanza el total de la venta.", menor.Message);

        // Suma mayor.
        var mayor = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago(MediosPago.Efectivo, precio + 1, precio + 1) },
                "op-pago-mayor",
                e.Sesion));
        Assert.Equal("El importe de los pagos supera el total de la venta.", mayor.Message);

        // Efectivo con recibido menor.
        var recibidoMenor = await Assert.ThrowsAsync<ArgumentException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago(MediosPago.Efectivo, precio, precio - 1) },
                "op-pago-rec",
                e.Sesion));
        Assert.Contains("El efectivo recibido es menor al importe correspondiente.", recibidoMenor.Message);

        // Recibido en medio no efectivo.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago(MediosPago.Debito, precio, precio) },
                "op-pago-rec2",
                e.Sesion));

        // Medio inexistente.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v1, 1, precio) },
                new[] { new NuevoPago("mercadopago", precio, null) },
                "op-pago-medio",
                e.Sesion));
    }

    [Fact]
    public async Task Mismo_operacion_id_no_duplica_venta()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        var lineas = new[] { new NuevaLineaVenta(v1, 1, precio) };
        var pagos = new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) };

        var primera = await e.Servicios.CasoConfirmarVenta.EjecutarAsync(lineas, pagos, "op-dup-001", e.Sesion);
        Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(primera);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoConfirmarVenta.EjecutarAsync(lineas, pagos, "op-dup-001", e.Sesion));
        Assert.Equal("Esta venta ya fue confirmada.", error.Message);
        Assert.Single(await e.Servicios.ConsultasVentas.BuscarResumenAsync(string.Empty, "todas", 10, 0, e.Sesion));

        // Reconciliación: la venta original se recupera por operación.
        var original = await e.Servicios.ConsultasVentas.BuscarPorOperacionAsync("op-dup-001", e.Sesion);
        Assert.NotNull(original);
        Assert.Equal("T-000001", original.Numero);
    }

    [Fact]
    public async Task Anulacion_total_restituye_stock_y_conserva_historial()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, v2) = await e.CatalogoConStockAsync();
        var p1 = await e.PrecioDeAsync(v1);
        var p2 = await e.PrecioDeAsync(v2);
        var total = p1 + 2 * p2;
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, p1), new NuevaLineaVenta(v2, 2, p2) },
            new[] { new NuevoPago(MediosPago.Efectivo, total, total) },
            "op-venta-anular",
            e.Sesion);

        await e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Cliente se arrepintió", "op-anul-001", e.Sesion);

        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal("anulada", ficha.Venta.Estado);
        Assert.Equal(2, ficha.Lineas.Count);
        Assert.Single(ficha.Pagos);
        Assert.NotNull(ficha.Anulacion);
        Assert.Equal("Cliente se arrepintió", ficha.Anulacion.Motivo);

        // Stock restituido a Disponible.
        Assert.Equal(10, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!.Disponible);
        Assert.Equal(7, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v2), e.Sesion))!.Disponible);
        Assert.NotEmpty(await e.AuditoriaDeAsync(ok.VentaId));
    }

    [Fact]
    public async Task Venta_no_consume_apartado_y_anulacion_lo_preserva()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        await e.Servicios.CasoApartarStock.EjecutarAsync(v1, 4, "Vidriera", "op-apartado-f04", e.Sesion);

        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 6, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, 6 * precio, 6 * precio) },
            "op-venta-apart",
            e.Sesion);

        var trasVenta = (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!;
        Assert.Equal(0, trasVenta.Disponible);
        Assert.Equal(4, trasVenta.Apartado);

        await e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Devolución", "op-anul-apart", e.Sesion);
        var trasAnulacion = (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!;
        Assert.Equal(6, trasAnulacion.Disponible);
        Assert.Equal(4, trasAnulacion.Apartado);
    }

    [Fact]
    public async Task Anulacion_exige_motivo_y_rechaza_doble()
    {
        using var e = await EntornoVentas.CrearAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            "op-venta-anular2",
            e.Sesion);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "  ", "op-anul-x", e.Sesion));

        await e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Error de carga", "op-anul-002", e.Sesion);
        var disponibleTrasAnular = (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!.Disponible;
        var doble = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Otra vez", "op-anul-003", e.Sesion));
        Assert.Equal("Esta venta ya fue anulada.", doble.Message);
        // La doble anulación fallida no duplica la restitución.
        Assert.Equal(disponibleTrasAnular, (await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1), e.Sesion))!.Disponible);
    }

    [Fact]
    public async Task Empleada_confirma_pero_no_anula_duena_si()
    {
        using var e = await EntornoVentas.CrearAsync();
        var sesionEmpleada = await e.SesionEmpleadaAsync();
        var (_, v1, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(v1);

        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v1, 1, precio) },
            new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
            "op-venta-emp",
            sesionEmpleada);
        Assert.Equal("T-000001", ok.Numero);

        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Quiero anular", "op-anul-emp", sesionEmpleada));

        await e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Autorizado por dueña", "op-anul-duena", e.Sesion);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal("anulada", ficha.Venta.Estado);
    }

    private sealed class EntornoVentas : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        private readonly string _dir;

        private EntornoVentas(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
        }

        public static async Task<EntornoVentas> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "ventas-f04.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[]
            {
                PermisosCatalogo.ProductosGestionar,
                PermisosCatalogo.VariantesGestionar,
                PermisosCatalogo.PreciosGestionar,
                PermisosInventario.CargaInicialEstablecer,
                PermisosInventario.NoVendibleGestionar,
                PermisosVentas.VentasConfirmar,
                PermisosVentas.VentasAnular,
            })
            {
                await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
            }

            return new EntornoVentas(dir, servicios, sesion.IdSesion);
        }

        public async Task<string> SesionEmpleadaAsync()
        {
            var id = await Servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("empleada", RolesLocal.Empleada), "clave-empleada-0000", Sesion);
            var sesion = await Servicios.CasoIniciarSesion.EjecutarAsync("empleada", "clave-empleada-0000");
            _ = id;
            return sesion.IdSesion;
        }

        /// <summary>Producto con 2 variantes (10 y 7 u.), precios 10000/12000.</summary>
        public async Task<(string Prod, string V1, string V2)> CatalogoConStockAsync()
        {
            var prod = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera F04", null, "cat-rem"), Sesion);
            var v1 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), Sesion);
            var v2 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "S", null), Sesion);
            await Servicios.CasoFijarPrecio.EjecutarAsync(v1, 10000, Sesion);
            await Servicios.CasoFijarPrecio.EjecutarAsync(v2, 12000, Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(v1, 10), new FilaCargaInicial(v2, 7) },
                Guid.NewGuid().ToString("N"),
                Sesion);
            return (prod, v1, v2);
        }

        public async Task<long> PrecioDeAsync(string varianteId)
        {
            var ficha = await Servicios.ConsultasCatalogo.ObtenerFichaAsync(
                (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, Sesion))!.ProductoId,
                DateTime.UtcNow.ToString("o"),
                Sesion);
            return ficha!.Variantes.First(v => v.Id == varianteId).PrecioMinor!.Value;
        }

        public async Task<string> CodigoDeAsync(string varianteId)
            => (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, Sesion))!.CodigoInterno;

        public async Task<IReadOnlyList<EventoAuditado>> AuditoriaDeAsync(string referenciaId)
        {
            await using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_dir, "ventas-f04.db"),
                Pooling = false,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT id, operacion_id, tipo_evento, actor_tipo, actor_id, referencia_tipo, referencia_id, detalle, creado_en FROM auditoria WHERE referencia_id = $ref ORDER BY creado_en, id;";
            comando.Parameters.AddWithValue("$ref", referenciaId);
            await using var lector = await comando.ExecuteReaderAsync();
            var resultado = new List<EventoAuditado>();
            while (await lector.ReadAsync())
            {
                resultado.Add(new EventoAuditado(
                    lector.GetString(0),
                    lector.GetString(1),
                    lector.GetString(2),
                    lector.GetString(3),
                    lector.IsDBNull(4) ? null : lector.GetString(4),
                    lector.IsDBNull(5) ? null : lector.GetString(5),
                    lector.IsDBNull(6) ? null : lector.GetString(6),
                    lector.IsDBNull(7) ? null : lector.GetString(7),
                    lector.GetString(8)));
            }

            return resultado;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
