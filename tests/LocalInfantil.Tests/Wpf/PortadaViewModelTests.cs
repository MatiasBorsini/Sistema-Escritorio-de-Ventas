using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Inicio;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Portada = pantalla Inicio real. Verifica que los indicadores se armen sólo
/// con datos reales (sin ceros inventados), que las acciones respeten los
/// permisos y que el refresco reactive se aplique sin bloquear.
/// </summary>
public sealed class PortadaViewModelTests
{
    private static ResumenInicio Resumen(
        VentasDelDiaInicio? ventas = null,
        RecepcionRecienteInicio? recepcion = null,
        int conteos = 0,
        ResumenInventarioInicio? inventario = null,
        IReadOnlySet<string>? acciones = null)
        => new(acciones ?? new HashSet<string>(), conteos, inventario ?? new(0, 0, 0), [], ventas, recepcion);

    private static PortadaViewModel Crear(ResumenInicio d, Func<Task<ResumenInicio>>? recargar = null, RelayCommand? vender = null, ICommand? irARecepciones = null)
        => new("María", d,
            new RelayCommand(_ => { }), new RelayCommand(_ => { }), new RelayCommand(_ => { }), new RelayCommand(_ => { }),
            recargar, new RelayCommand(_ => { }), irARecepciones ?? new RelayCommand(_ => { }), vender);

    [Fact]
    public void Sin_datos_muestra_indicadores_honestos_sin_ventas()
    {
        var vm = Crear(Resumen(inventario: new(10, 8, 2)));

        Assert.False(vm.HayVentasDelDia);
        Assert.False(vm.HayRecepcionReciente);
        Assert.False(vm.HayConteosPendientes);
        Assert.DoesNotContain(vm.Indicadores, i => i.Titulo == "Ventas de hoy");
        Assert.DoesNotContain(vm.Indicadores, i => i.Titulo == "Conteos pendientes");
        Assert.Contains(vm.Indicadores, i => i.Titulo == "Inventario disponible" && i.Valor == "8");
        Assert.Contains(vm.Indicadores, i => i.Titulo == "Recepción más reciente" && i.Valor == "Sin recepciones");
    }

    [Fact]
    public void Con_datos_muestra_importe_neto_y_recepcion_real()
    {
        var vm = Crear(Resumen(
            ventas: new(123450, 3),
            recepcion: new("Ropa Sur", 7, "2026-09-24T11:00:00Z"),
            conteos: 2,
            inventario: new(14, 10, 4)));

        Assert.True(vm.HayVentasDelDia);
        Assert.Equal("ARS 1.234,50", vm.Indicadores[0].Valor);
        Assert.Equal("3 operaciones · neto vendido", vm.Indicadores[0].Detalle);
        Assert.Equal(3, vm.Indicadores.Count);
        Assert.Equal("Ropa Sur", vm.Indicadores[2].Valor);
        Assert.Contains("7 unidades", vm.Indicadores[2].Detalle);
        Assert.True(vm.HayConteosPendientes);
        Assert.Equal("2 conteos esperan revisión", vm.TextoConteosPendientes);
        Assert.True(vm.HayApartado);
        Assert.Equal("4 unidades están apartadas", vm.TextoApartado);
    }

    [Fact]
    public void Acciones_respetan_los_permisos_recibidos()
    {
        var permitidas = new HashSet<string> { ConsultasInicio.RecibirMercaderia, ConsultasInicio.ConteoFisico };
        var vm = Crear(Resumen(acciones: permitidas));

        Assert.Contains(vm.Acciones, a => a.Titulo == "Recepción de mercadería");
        Assert.Contains(vm.Acciones, a => a.Titulo == "Conteo físico");
        Assert.Contains(vm.Acciones, a => a.Titulo == "Consultar inventario");
        Assert.DoesNotContain(vm.Acciones, a => a.Titulo == "Nuevo producto");
        Assert.DoesNotContain(vm.Acciones, a => a.Titulo == "Realizar venta");
    }

    [Fact]
    public void Realizar_venta_va_primero_y_ejecuta_el_comando_del_pos()
    {
        var abrirPos = false;
        var vender = new RelayCommand(_ => abrirPos = true);
        var vm = Crear(Resumen(), vender: vender);

        Assert.Equal("Realizar venta", vm.Acciones[0].Titulo);
        Assert.Equal("ventas", vm.Acciones[0].Clave);
        vm.Acciones[0].Comando.Execute(null);
        Assert.True(abrirPos);

        vm.Acciones[^1].Comando.Execute(null);
    }

    [Fact]
    public void IrARecepciones_esta_publicado_con_el_comando_del_shell()
    {
        bool llamado = false;
        var ir = new RelayCommand(_ => llamado = true);
        var vm = Crear(Resumen(), irARecepciones: ir);

        Assert.Same(ir, vm.IrARecepciones);
        vm.IrARecepciones.Execute(null);
        Assert.True(llamado);
    }

    [Fact]
    public async Task Refrescar_vuelve_a_leer_y_actualiza_los_indicadores()
    {
        var llamadas = 0;
        ResumenInicio actual = Resumen(inventario: new(5, 5, 0));
        var vm = Crear(actual, () =>
        {
            llamadas++;
            actual = Resumen(inventario: new(9, 8, 1), conteos: 2, ventas: new(100, 1));
            return Task.FromResult(actual);
        });

        await vm.RefrescarAsync();

        Assert.Equal(1, llamadas);
        Assert.Equal(8, vm.Inventario.Disponible);
        Assert.Equal(2, vm.ConteosPendientes);
        Assert.Equal("ARS 1,00", vm.Indicadores[0].Valor);
        Assert.False(vm.Ocupado);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Un_error_al_refrescar_conserva_los_ultimos_datos_conocidos()
    {
        var vm = Crear(Resumen(inventario: new(5, 5, 0)),
            () => Task.FromException<ResumenInicio>(new InvalidOperationException("sin conexión")));

        await vm.RefrescarAsync();

        Assert.Equal(5, vm.Inventario.Fisico);
        Assert.NotNull(vm.Error);
        Assert.False(vm.Ocupado);
    }
}
