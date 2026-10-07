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

    private sealed class DialogosVentasFake : IDialogosVentas
    {
        public string? UltimoTicket { get; private set; }
        public void MostrarTicket(string ventaId) => UltimoTicket = ventaId;
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
