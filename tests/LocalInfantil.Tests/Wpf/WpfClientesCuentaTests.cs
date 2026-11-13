using LocalInfantil.Application.Contracts;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfClientesCuentaTests
{
    [Fact]
    public async Task Alta_persiste_muestra_y_selecciona_el_cliente_sin_reintento_duplicado()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var vm = new ClientesViewModel(e.Servicios, () => e.Sesion, true);
        await vm.CargarAsync(true);
        vm.Busqueda = "búsqueda anterior";
        vm.IncluirInactivos = true;
        vm.NuevoCommand.Execute(null);
        vm.Nombre = "Cliente recién creado";
        vm.Telefono = "11-5555-0123";

        vm.GuardarCommand.Execute(null);
        vm.GuardarCommand.Execute(null); // La misma acción en curso no se puede reejecutar.
        await EsperarAsync(() => vm.Mensaje == "El cliente se creó correctamente.");

        Assert.False(vm.FormularioVisible);
        var listado = Assert.Single(vm.Clientes, c => c.NombreCompleto == "Cliente recién creado");
        Assert.Equal(listado.Id, vm.Seleccionado?.Id);
        Assert.Equal("Cliente recién creado", vm.Nombre);
        Assert.Equal(string.Empty, vm.Busqueda);
        Assert.True(vm.IncluirInactivos);
        Assert.Equal("Página 1", vm.TextoPagina);

        var persistido = await e.Servicios.ConsultasClientes.ObtenerAsync(listado.Id, e.Sesion);
        Assert.Equal("11-5555-0123", persistido?.Telefono);
        Assert.Single(await e.Servicios.ConsultasClientes.BuscarAsync("Cliente recién", false, 10, 0, e.Sesion));
        Assert.Single(await e.Servicios.ConsultasClientes.BuscarAsync("11-5555", false, 10, 0, e.Sesion));
    }

    [Fact]
    public async Task Listado_de_cuenta_busca_por_nombre_y_telefono_y_selecciona_fila()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("María López", "11-7777-0101", null), e.Sesion);
        var idSegundo = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("María Pérez", "11-8888-0202", null), e.Sesion);
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.Todos;
        vm.TextoCliente = "María";
        await vm.CargarClientesAsync();
        Assert.Equal(2, vm.Clientes.Count);
        // DataGrid actualiza SelectedItem tanto al hacer clic como al navegar
        // con flechas; ambos caminos usan esta misma propiedad enlazada.
        vm.ClienteListadoSeleccionado = vm.Clientes.Single(x => x.Id == idSegundo);
        Assert.Equal(idSegundo, vm.Cliente?.Id);
        Assert.Equal("María", vm.TextoCliente);
        Assert.Equal(2, vm.Clientes.Count);
        Assert.Equal(1, vm.PaginaClientes);

        vm.ClienteListadoSeleccionado = vm.Clientes.Single(x => x.Id == id);
        Assert.Equal(id, vm.Cliente?.Id);
        Assert.Equal("María", vm.TextoCliente);
        Assert.Equal(2, vm.Clientes.Count);

        vm.TextoCliente = "11-7777";
        await vm.CargarClientesAsync();
        var porTelefono = Assert.Single(vm.Clientes);
        Assert.Equal(id, porTelefono.Id);
        vm.ClienteListadoSeleccionado = porTelefono;
        Assert.Equal(id, vm.Cliente?.Id);
        Assert.Equal("11-7777", vm.TextoCliente);
        Assert.True(vm.FormularioAbono == false && vm.FormularioAjuste == false);

        vm.TextoCliente = string.Empty;
        await vm.CargarClientesAsync();
        Assert.Equal(2, vm.Clientes.Count);
        Assert.Equal(string.Empty, vm.TextoCliente);
    }

    [Fact]
    public async Task Seleccion_con_formulario_editado_advierte_y_no_descarta_cambios()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var idA = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente A", null, null), e.Sesion);
        var idB = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente B", null, null), e.Sesion);
        var vm = new ClientesViewModel(e.Servicios, () => e.Sesion, true);
        await vm.CargarAsync(true);

        vm.Seleccionado = vm.Clientes.Single(c => c.Id == idA);
        vm.EditarCommand.Execute(null);
        Assert.True(vm.FormularioVisible);
        vm.Nombre = "Cliente A modificado";
        Assert.True(vm.HayCambiosSinGuardar);

        // La alta nueva tampoco puede descartar el formulario de edición por un atajo.
        vm.NuevoCommand.Execute(null);
        Assert.True(vm.ConfirmacionCambiosVisible);
        Assert.Equal(idA, vm.Seleccionado!.Id);
        vm.VolverAEditarCommand.Execute(null);
        Assert.Equal("Cliente A modificado", vm.Nombre);

        // Intento de cambiar de cliente con cambios sin guardar: se advierte y no se aplica.
        vm.Seleccionado = vm.Clientes.Single(c => c.Id == idB);
        Assert.True(vm.ConfirmacionCambiosVisible);
        Assert.Equal(idA, vm.Seleccionado!.Id);

        // Seguir editando mantiene la selección original.
        vm.VolverAEditarCommand.Execute(null);
        Assert.False(vm.ConfirmacionCambiosVisible);
        Assert.Equal(idA, vm.Seleccionado!.Id);

        // Descartar y continuar aplica la selección pendiente y cierra el formulario.
        vm.Seleccionado = vm.Clientes.Single(c => c.Id == idB);
        Assert.True(vm.ConfirmacionCambiosVisible);
        vm.ConfirmarPerderCambiosCommand.Execute(null);
        Assert.False(vm.ConfirmacionCambiosVisible);
        Assert.Equal(idB, vm.Seleccionado!.Id);
        Assert.False(vm.FormularioVisible);
    }

    [Fact]
    public async Task Alternar_incluir_inactivos_y_busqueda_no_falla_sin_app()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var activoId = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente anterior", "11-9999", null), e.Sesion);
        await Task.Delay(20);
        var inactivoId = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente reciente inactivo", "11-8888", null), e.Sesion);
        await e.Servicios.CasoDesactivarCliente.EjecutarAsync(inactivoId, e.Sesion);
        var vm = new ClientesViewModel(e.Servicios, () => e.Sesion, true);
        await vm.CargarAsync(true);
        Assert.Contains(vm.Clientes, c => c.Id == activoId);
        Assert.DoesNotContain(vm.Clientes, c => c.Id == inactivoId);
        // Sin Application.Current no se arma el temporizador del debounce: no dispara y no cuelga.
        vm.Busqueda = "zzz-sin-resultados";
        vm.IncluirInactivos = true;
        await vm.CargarAsync(true);
        Assert.Empty(vm.Clientes);
        vm.Busqueda = "";
        await vm.CargarAsync(true);
        Assert.Equal(inactivoId, vm.Clientes[0].Id);
        Assert.Contains(vm.Clientes, c => c.Id == activoId);
    }

    [Fact]
    public async Task Cliente_abre_en_consulta_y_cancelar_edicion_no_persiste()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Consulta inicial", "11-3333", null), e.Sesion);
        var vm = new ClientesViewModel(e.Servicios, () => e.Sesion, true);
        await vm.CargarAsync(true);

        Assert.False(vm.FormularioVisible);
        vm.Seleccionado = vm.Clientes.Single(c => c.Id == id);
        vm.EditarCommand.Execute(null);
        vm.Nombre = "Nombre no guardado";
        vm.CancelarEdicionCommand.Execute(null);

        var persisted = Assert.Single(await e.Servicios.ConsultasClientes.BuscarAsync("11-3333", false, 10, 0, e.Sesion));
        Assert.Equal("Consulta inicial", persisted.NombreCompleto);
        Assert.False(vm.FormularioVisible);
        Assert.Equal("Consulta inicial", vm.Nombre);
    }

    [Fact]
    public async Task Cambio_de_cliente_en_cuenta_cierra_formularios_editor_limite_y_previews()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var idA = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cuenta A", "11-1000", null), e.Sesion);
        var idB = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cuenta B", "11-2000", null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(idA, 10_000, e.Sesion);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(idA, true, 1_000, "Saldo para prueba de abono", 0, "cuenta-vm-prueba-abono", e.Sesion));
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);

        await SeleccionarFilaAsync(vm, idA);
        Assert.NotNull(vm.Cliente);
        Assert.True(vm.HayCliente);
        await vm.CargarAsync();

        vm.AbrirEdicionLimiteCommand.Execute(null);
        Assert.True(vm.EdicionLimiteVisible);
        vm.AbrirAbonoCommand.Execute(null);
        Assert.True(vm.FormularioAbono);

        var sugerenciaB = Assert.Single(await e.Servicios.ConsultasClientes.BuscarAsync("11-2000", false, 10, 0, e.Sesion));
        Assert.NotEqual(idA, sugerenciaB.Id);
        await SeleccionarFilaAsync(vm, idB);
        Assert.False(vm.EdicionLimiteVisible);
        Assert.False(vm.FormularioAbono);
        Assert.False(vm.FormularioAjuste);
        Assert.Null(vm.PreviewAbono);
        Assert.Empty(vm.AplicacionesPrevias);
    }

    [Fact]
    public async Task Cuenta_limpia_datos_anteriores_y_protege_limite_editado_al_cambiar_cliente()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var idA = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cuenta A", "11-1000", null), e.Sesion);
        var idB = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cuenta B", "11-2000", null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(idA, 30_000, e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(idB, 50_000, e.Sesion);
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        await SeleccionarFilaAsync(vm, idA);
        await vm.CargarAsync();
        Assert.Equal(30_000, vm.Resumen!.LimiteMinor);
        vm.AbrirEdicionLimiteCommand.Execute(null);
        vm.LimiteTexto = "400,00";

        await SeleccionarFilaAsync(vm, idB);
        Assert.True(vm.ConfirmacionCambioClienteVisible);
        Assert.Equal(idA, vm.Cliente!.Id);
        vm.CancelarCambioClienteCommand.Execute(null);
        Assert.Equal(idA, vm.Cliente!.Id);

        await SeleccionarFilaAsync(vm, idB);
        vm.ConfirmarCambioClienteCommand.Execute(null);
        Assert.Equal(idB, vm.Cliente!.Id);
        Assert.True(vm.Resumen is null || vm.Resumen.ClienteId == idB);
        Assert.Empty(vm.Movimientos);
        Assert.False(vm.EdicionLimiteVisible);

        await vm.CargarAsync();
        Assert.Equal(50_000, vm.Resumen!.LimiteMinor);
    }

    [Fact]
    public async Task Cuenta_lista_prioriza_deuda_aunque_el_fiado_este_deshabilitado_y_pagina_en_sql()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var debt = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cuenta con deuda", "11-4000", null), e.Sesion);
        var enabled = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cuenta habilitada", "11-5000", null), e.Sesion);
        var empty = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cuenta vacía", "11-6000", null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(debt, 30_000, e.Sesion);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(debt, true, 12_500, "Deuda de prueba", 0, "ui-deuda", e.Sesion));
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(debt, null, e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(enabled, 40_000, e.Sesion);

        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        await vm.CargarClientesAsync();
        Assert.Contains(vm.Clientes, x => x.Id == debt && x.Estado.Contains("Con deuda", StringComparison.Ordinal));
        Assert.Contains(vm.Clientes, x => x.Id == enabled && x.Estado == "Fiado habilitado");
        Assert.Contains(vm.Clientes, x => x.Id == empty && x.LimiteMinor is null && x.SaldoMinor == 0);
        Assert.Equal(LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.Todos, vm.FiltroClientes);

        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.Relevantes;
        await vm.CargarClientesAsync();
        Assert.DoesNotContain(vm.Clientes, x => x.Id == empty);

        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.ConDeuda;
        await vm.CargarClientesAsync();
        Assert.Single(vm.Clientes);
        Assert.Equal(debt, vm.Clientes[0].Id);

        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.FiadoHabilitado;
        await vm.CargarClientesAsync();
        Assert.Single(vm.Clientes);
        Assert.Equal(enabled, vm.Clientes[0].Id);

        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.Todos;
        vm.TextoCliente = "11-6000";
        await vm.CargarClientesAsync();
        Assert.Single(vm.Clientes);
        Assert.Equal(empty, vm.Clientes[0].Id);
    }

    [Fact]
    public async Task Cuenta_busqueda_y_filtro_se_combinan_con_paginacion_sql_y_reinician_en_primera_pagina()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        for (var i = 1; i <= 21; i++)
            await e.Servicios.CasoCrearCliente.EjecutarAsync(new($"Cliente paginado {i:00}", $"11-55{i:0000}", null), e.Sesion);

        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        vm.TextoCliente = "Cliente paginado";
        await vm.CargarClientesAsync();
        Assert.Equal(1, vm.PaginaClientes);
        Assert.Equal(20, vm.Clientes.Count);
        Assert.True(vm.HayMasClientes);

        vm.ClientesPaginaSiguienteCommand.Execute(null);
        await vm.CargarClientesAsync();
        Assert.Equal(2, vm.PaginaClientes);
        Assert.Single(vm.Clientes);
        Assert.False(vm.HayMasClientes);

        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.ConDeuda;
        await vm.CargarClientesAsync();
        Assert.Equal(1, vm.PaginaClientes);
        Assert.Empty(vm.Clientes);
        Assert.False(vm.HayMasClientes);
    }

    [Fact]
    public async Task Cuenta_habilita_con_confirmacion_positiva_y_deshabilita_sin_borrar_deuda()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente para habilitar", null, null), e.Sesion);
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(id, 20_000, e.Sesion);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(id, true, 8_000, "Deuda previa", 0, "ui-deuda-previa", e.Sesion));
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(id, null, e.Sesion);
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        await SeleccionarFilaAsync(vm, id);
        Assert.True(vm.NoHabilitadoFiado);
        Assert.False(vm.PuedeAjustarCuenta);
        vm.AbrirAjusteCommand.Execute(null);
        Assert.False(vm.FormularioAjuste);
        Assert.Equal(8_000, vm.Resumen!.SaldoMinor);

        vm.AbrirEdicionLimiteCommand.Execute(null);
        vm.LimiteTexto = "300,00";
        vm.GuardarLimiteCommand.Execute(null);
        await EsperarAsync(() => vm.ConfirmacionHabilitarFiadoVisible);
        Assert.Null((await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(id, e.Sesion))!.LimiteMinor);
        vm.ConfirmarHabilitarFiadoCommand.Execute(null);
        await EsperarAsync(async () => (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(id, e.Sesion))!.LimiteMinor == 30_000);
        await vm.CargarAsync();
        Assert.Equal(8_000, vm.Resumen!.SaldoMinor);
        Assert.True(vm.PuedeAjustarCuenta);
        vm.AbrirAjusteCommand.Execute(null);
        Assert.True(vm.FormularioAjuste);
        vm.CancelarFormularioCommand.Execute(null);

        vm.AbrirEdicionLimiteCommand.Execute(null);
        vm.DeshabilitarFiadoCommand.Execute(null);
        Assert.True(vm.ConfirmacionDeshabilitarFiadoVisible);
        vm.ConfirmarDeshabilitarFiadoCommand.Execute(null);
        await EsperarAsync(async () => (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(id, e.Sesion))!.LimiteMinor is null);
        Assert.Equal(8_000, (await e.Servicios.ConsultasCuentaCorriente.ObtenerAsync(id, e.Sesion))!.SaldoMinor);
        Assert.False(vm.PuedeAjustarCuenta);
    }

    [Fact]
    public async Task Cuenta_no_abre_abono_con_saldo_cero_y_advierte_al_cambiar_operacion_editada()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new("Cliente sin deuda", null, null), e.Sesion);
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, true, true, true, true);
        await SeleccionarFilaAsync(vm, id);
        Assert.False(vm.PuedeRegistrarAbono);
        vm.AbrirAbonoCommand.Execute(null);
        Assert.False(vm.FormularioAbono);

        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(id, 2_000, e.Sesion);
        await e.Servicios.CasoAjustarCuentaCorriente.EjecutarAsync(new(id, true, 1_000, "Deuda de prueba", 0, "ui-deuda-abono", e.Sesion));
        await e.Servicios.CasoConfigurarLimiteCredito.EjecutarAsync(id, null, e.Sesion);
        await vm.CargarAsync();
        vm.AbrirAbonoCommand.Execute(null);
        Assert.True(vm.FormularioAbono);
        vm.ImporteAbono = "500,00";
        vm.AbrirAjusteCommand.Execute(null);
        Assert.False(vm.ConfirmacionCambioOperacionVisible);
        Assert.True(vm.FormularioAbono);
        Assert.False(vm.FormularioAjuste);
    }

    [Fact]
    public async Task Lista_de_cuenta_respeta_permiso_de_consulta()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var vm = new CuentaCorrienteViewModel(e.Servicios, () => e.Sesion, false, false, false, false);
        await vm.CargarClientesAsync();
        Assert.Empty(vm.Clientes);
        Assert.False(vm.PuedeRegistrarAbono);
        Assert.False(vm.PuedeConfigurarLimite);
    }

    private static async Task EsperarAsync(Func<bool> condicion)
    {
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condicion() && DateTime.UtcNow < limite) await Task.Delay(10);
        Assert.True(condicion(), "La operación asíncrona de alta no terminó dentro del límite de la prueba.");
    }

    private static async Task SeleccionarFilaAsync(CuentaCorrienteViewModel vm, string id)
    {
        var clienteAnterior = vm.Cliente?.Id;
        vm.FiltroClientes = LocalInfantil.Application.Contracts.FiltroClientesCuentaCorriente.Todos;
        vm.TextoCliente = string.Empty;
        await vm.CargarClientesAsync();
        vm.ClienteListadoSeleccionado = Assert.Single(vm.Clientes, x => x.Id == id);
        if (vm.Cliente?.Id != clienteAnterior) await vm.CargarAsync();
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicion)
    {
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < limite && !await condicion()) await Task.Delay(10);
        Assert.True(await condicion(), "La operación asíncrona no terminó dentro del límite de la prueba.");
    }
}
