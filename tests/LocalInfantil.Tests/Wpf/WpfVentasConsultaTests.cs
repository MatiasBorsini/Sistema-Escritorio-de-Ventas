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
/// F04: consulta master/detail de ventas, ticket y anulación desde la UI.
/// </summary>
public sealed class WpfVentasConsultaTests
{
    [Fact]
    public async Task Buscar_lista_y_ficha_muestran_ventas()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var numero = await e.VenderAsync(2, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Single(vm.Historial);
        Assert.Equal(numero, vm.Historial[0].Resumen.Numero);

        vm.FilaSeleccionada = vm.Historial[0];
        await vm.CargarFichaAsyncPublic(vm.Historial[0].Resumen.Id);
        Assert.True(vm.HayFicha);
        Assert.Equal(numero, vm.Ficha!.Venta.Numero);
        Assert.Equal(20000, vm.Ficha.Venta.TotalMinor);
        Assert.Single(vm.Ficha.Lineas);
        Assert.True(vm.PuedeAnular);
    }

    [Fact]
    public async Task Anular_pide_motivo_y_actualiza_fila()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var numero = await e.VenderAsync(1, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsyncPublic();
        vm.FilaSeleccionada = vm.Historial[0];
        await vm.CargarFichaAsyncPublic(vm.Historial[0].Resumen.Id);

        vm.SolicitarAnulacionCommand.Execute(null);
        Assert.True(vm.ConfirmacionVisible);
        await vm.ConfirmarAnulacionAsyncPublic();
        Assert.Equal("El motivo es obligatorio.", vm.Error);

        vm.Motivo = "Se equivocó de talle";
        await vm.ConfirmarAnulacionAsyncPublic();
        Assert.Null(vm.Error);
        Assert.Contains("anulada", vm.Mensaje ?? string.Empty);
        Assert.False(vm.PuedeAnular);
        Assert.True(vm.Historial[0].Anulada);
        Assert.Equal("Anulada", vm.Historial[0].EstadoTexto);
    }

    [Fact]
    public async Task Ver_ticket_abre_dialogo()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        await e.VenderAsync(1, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsyncPublic();
        vm.FilaSeleccionada = vm.Historial[0];
        await vm.CargarFichaAsyncPublic(vm.Historial[0].Resumen.Id);
        await vm.VerTicketAsyncPublic();
        Assert.NotNull(e.Dialogos.UltimoTicket);
    }

    private sealed class DialogosVentasFake : IDialogosVentas
    {
        public string? UltimoTicket { get; private set; }
        public void MostrarTicket(string ventaId) => UltimoTicket = ventaId;
    }

    private sealed class EntornoVentasUi : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public DialogosVentasFake Dialogos { get; } = new();
        private readonly string _dir;

        private EntornoVentasUi(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
        }

        public static async Task<EntornoVentasUi> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "ventas-ui-f04.db");
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

            return new EntornoVentasUi(dir, servicios, sesion.IdSesion);
        }

        public async Task<string> VenderAsync(long cantidad, long precio)
        {
            var prod = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera Consulta", null, "cat-rem"), Sesion);
            var v = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), Sesion);
            await Servicios.CasoFijarPrecio.EjecutarAsync(v, precio, Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(v, 10) }, Guid.NewGuid().ToString("N"), Sesion);
            var ok = (ResultadoConfirmacionVenta.Confirmada)await Servicios.CasoConfirmarVenta.EjecutarAsync(
                new[] { new NuevaLineaVenta(v, cantidad, precio) },
                new[] { new NuevoPago(MediosPago.Efectivo, cantidad * precio, cantidad * precio) },
                Guid.NewGuid().ToString("N"),
                Sesion);
            return ok.Numero;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
