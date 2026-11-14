using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// F04: consulta master/detail de ventas, ticket y anulación desde la UI.
/// </summary>
public sealed class WpfVentasConsultaTests
{
    [Fact]
    public async Task Activar_Ventas_carga_primera_pagina_y_detalle_sin_pulsar_Buscar()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var numero = await e.VenderAsync(2, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        using var coordinador = new CoordinadorActualizacionVistas(e.Servicios.NotificadorCambios);
        coordinador.Registrar(vm, [AreaDatos.Ventas, AreaDatos.Cambios], vm.CargarAsyncPublic,
            () => !vm.Ocupado && vm.FilaSeleccionada is null && !vm.ConfirmacionVisible,
            () => vm.Error is not null, iniciarDesactualizada: true);
        await coordinador.ActivarAsync(vm);

        Assert.Null(vm.Error);
        Assert.Single(vm.Historial);
        Assert.Equal(numero, vm.Historial[0].Resumen.Numero);
        Assert.Same(vm.Historial[0], vm.FilaSeleccionada);
        Assert.True(vm.HayFicha);
        Assert.Equal(numero, vm.Ficha!.Venta.Numero);
        Assert.Equal(20000, vm.Ficha.Venta.TotalMinor);
        Assert.Single(vm.Ficha.Lineas);
        Assert.True(vm.PuedeAnular);
        Assert.False(vm.MostrarEstadoVacio);
        Assert.False(vm.MostrarEstadoSinSeleccion);
    }

    [Fact]
    public async Task Activar_Ventas_sin_registros_muestra_estados_vacios()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        using var coordinador = new CoordinadorActualizacionVistas(e.Servicios.NotificadorCambios);
        coordinador.Registrar(vm, [AreaDatos.Ventas, AreaDatos.Cambios], vm.CargarAsyncPublic,
            () => !vm.Ocupado && vm.FilaSeleccionada is null && !vm.ConfirmacionVisible,
            () => vm.Error is not null, iniciarDesactualizada: true);

        await coordinador.ActivarAsync(vm);

        Assert.Null(vm.Error);
        Assert.Empty(vm.Historial);
        Assert.True(vm.MostrarEstadoVacio);
        Assert.Equal("Todavía no hay ventas registradas.", vm.TextoEstadoVacio);
        Assert.True(vm.MostrarEstadoSinSeleccion);
        Assert.Equal("No hay una venta seleccionada.", vm.TextoEstadoSinSeleccion);
        Assert.Null(vm.Ficha);
    }

    [Fact]
    public async Task Buscar_manual_aplica_texto_y_filtro_de_estado()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var confirmada = await e.VenderAsync(1, 10000);
        var anulada = await e.VenderAsync(1, 10000);
        var ventaAnulada = await e.Servicios.ConsultasVentas.BuscarPorNumeroAsync(anulada, e.Sesion);
        Assert.NotNull(ventaAnulada);
        await e.Servicios.CasoAnularVenta.EjecutarAsync(
            ventaAnulada!.Id, "Prueba de filtro", Guid.NewGuid().ToString("N"), e.Sesion);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);

        vm.TextoBusqueda = confirmada;
        vm.FiltroEstado = FiltroVentas.Confirmadas;
        await vm.CargarAsyncPublic();
        Assert.Single(vm.Historial);
        Assert.Equal(confirmada, vm.Historial[0].Resumen.Numero);
        Assert.Equal("Confirmada", vm.EstadoFichaTexto);

        vm.TextoBusqueda = anulada;
        vm.FiltroEstado = FiltroVentas.Anuladas;
        await vm.CargarAsyncPublic();
        Assert.Single(vm.Historial);
        Assert.Equal(anulada, vm.Historial[0].Resumen.Numero);
        Assert.Equal("Anulada", vm.EstadoFichaTexto);
        Assert.False(vm.PuedeAnular);
    }

    [Fact]
    public async Task Rango_de_fechas_incluye_los_dos_limites_del_dia_y_filtra_antes_de_paginar()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var anterior = await e.VenderAsync(1, 10000);
        var medianoche = await e.VenderAsync(1, 10000);
        var finDelDia = await e.VenderAsync(1, 10000);
        var siguienteDia = await e.VenderAsync(1, 10000);
        var dia = DateOnly.FromDateTime(DateTime.Today.AddDays(-2));
        await e.FijarFechaAsync(anterior, dia.AddDays(-1).ToDateTime(new TimeOnly(23, 59)));
        await e.FijarFechaAsync(medianoche, dia.ToDateTime(TimeOnly.MinValue));
        await e.FijarFechaAsync(finDelDia, dia.ToDateTime(new TimeOnly(23, 59, 59)));
        await e.FijarFechaAsync(siguienteDia, dia.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var desde = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 10, 0, e.Sesion, desde: dia);
        Assert.Equal(3, desde.Count);
        var desdePaginado = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 1, 0, e.Sesion, desde: dia);
        Assert.Equal(2, desdePaginado.Count); // página + peek; no incluye la venta anterior al rango
        Assert.DoesNotContain(desdePaginado, v => v.Numero == anterior);
        var hasta = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 10, 0, e.Sesion, hasta: dia);
        Assert.Equal(3, hasta.Count);
        var rangoCompleto = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 10, 0, e.Sesion, desde: dia, hasta: dia.AddDays(1));
        Assert.Equal(3, rangoCompleto.Count);
        var mismoDia = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 10, 0, e.Sesion, desde: dia, hasta: dia);
        Assert.Equal(new[] { finDelDia, medianoche }.OrderBy(x => x), mismoDia.Select(x => x.Numero).OrderBy(x => x));
        var sinResultados = await e.Servicios.ConsultasVentas.BuscarResumenAsync("", "todas", 10, 0, e.Sesion, desde: dia.AddDays(3), hasta: dia.AddDays(4));
        Assert.Empty(sinResultados);
    }

    [Fact]
    public async Task Busca_por_nombre_y_codigo_y_combina_estado_con_fecha()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var confirmada = await e.VenderAsync(1, 10000);
        var anulada = await e.VenderAsync(1, 10000);
        var dia = DateOnly.FromDateTime(DateTime.Today);
        await e.FijarFechaAsync(confirmada, dia.ToDateTime(new TimeOnly(11, 0)));
        await e.FijarFechaAsync(anulada, dia.ToDateTime(new TimeOnly(12, 0)));
        var anuladaRegistrada = (await e.Servicios.ConsultasVentas.BuscarPorNumeroAsync(anulada, e.Sesion))!;
        await e.Servicios.CasoAnularVenta.EjecutarAsync(anuladaRegistrada.Id, "test de filtro", Guid.NewGuid().ToString("N"), e.Sesion);

        var porProducto = await e.Servicios.ConsultasVentas.BuscarResumenAsync("Remera Consulta", "confirmadas", 10, 0, e.Sesion, desde: dia, hasta: dia);
        Assert.Equal(confirmada, Assert.Single(porProducto).Numero);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(anuladaRegistrada.Id, e.Sesion))!;
        var codigo = Assert.Single(ficha.Lineas).CodigoInterno;
        var porCodigo = await e.Servicios.ConsultasVentas.BuscarResumenAsync(codigo, "anuladas", 10, 0, e.Sesion, desde: dia, hasta: dia);
        Assert.Equal(anulada, Assert.Single(porCodigo).Numero);
    }

    [Fact]
    public async Task Filtros_reactivos_borrado_de_texto_y_paginacion_conservan_los_otros_criterios()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var dentroA = await e.VenderAsync(1, 10000);
        var dentroB = await e.VenderAsync(1, 10000);
        var fuera = await e.VenderAsync(1, 10000);
        var dia = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
        await e.FijarFechaAsync(dentroA, dia.ToDateTime(new TimeOnly(10, 0)));
        await e.FijarFechaAsync(dentroB, dia.ToDateTime(new TimeOnly(11, 0)));
        await e.FijarFechaAsync(fuera, dia.AddDays(-1).ToDateTime(new TimeOnly(11, 0)));
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        vm.TamanoPagina = 1;
        vm.Desde = dia.ToDateTime(TimeOnly.MinValue);
        vm.Hasta = dia.ToDateTime(TimeOnly.MinValue);
        vm.FiltroEstado = FiltroVentas.Confirmadas;
        vm.TextoBusqueda = "T-";
        await vm.EsperarBusquedaAsyncPublic();
        Assert.Single(vm.Historial);
        Assert.True(vm.HaySiguiente);
        await vm.SiguienteAsyncPublic();
        Assert.Equal("Página 2", vm.PaginaTexto);
        Assert.Single(vm.Historial);

        vm.Hasta = dia.AddDays(1).ToDateTime(TimeOnly.MinValue);
        await vm.EsperarBusquedaAsyncPublic();
        Assert.Equal("Página 1", vm.PaginaTexto);

        vm.TextoBusqueda = "";
        await vm.EsperarBusquedaAsyncPublic();
        Assert.Equal("Página 1", vm.PaginaTexto);
        Assert.Equal(dentroB, vm.FilaSeleccionada!.Resumen.Numero);
        Assert.True(vm.HaySiguiente);
        Assert.Equal("Confirmada", vm.EstadoFichaTexto);
        Assert.Equal(dia, DateOnly.FromDateTime(vm.Desde!.Value));
        Assert.Equal(dia.AddDays(1), DateOnly.FromDateTime(vm.Hasta!.Value));

        await vm.LimpiarFiltrosAsyncPublic();
        Assert.Null(vm.Desde);
        Assert.Null(vm.Hasta);
        Assert.Equal(FiltroVentas.Todas, vm.FiltroEstado);
        Assert.Equal(string.Empty, vm.TextoBusqueda);
        Assert.Single(vm.Historial);
        Assert.True(vm.HaySiguiente);

        vm.TextoBusqueda = "sin-coincidencias";
        await vm.EsperarBusquedaAsyncPublic();
        Assert.Empty(vm.Historial);
        Assert.Null(vm.Ficha);
        Assert.True(vm.MostrarEstadoVacio);
        Assert.Equal("No hay ventas que coincidan con estos filtros.", vm.TextoEstadoVacio);
    }

    [Fact]
    public async Task Rango_invalido_no_consulta_y_limpia_detalle_obsoleto()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        await e.VenderAsync(1, 10000);
        var llamadas = 0;
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos,
            (texto, estado, limite, desplazamiento, sesion, desde, hasta, ct) =>
            {
                llamadas++;
                return e.Servicios.ConsultasVentas.BuscarResumenAsync(texto, estado, limite, desplazamiento, sesion, ct, desde, hasta);
            });
        await vm.CargarAsyncPublic();
        Assert.NotNull(vm.Ficha);
        vm.Desde = DateTime.Today;
        await vm.EsperarBusquedaAsyncPublic();
        var llamadasAntes = llamadas;
        vm.Hasta = DateTime.Today.AddDays(-1);
        await vm.EsperarBusquedaAsyncPublic();
        Assert.Equal(llamadasAntes, llamadas);
        Assert.Equal("La fecha Desde no puede ser posterior a la fecha Hasta.", vm.Error);
        Assert.Null(vm.Ficha);
        Assert.Empty(vm.Historial);
    }

    [Fact]
    public async Task Respuesta_vieja_no_pisa_una_busqueda_mas_nueva()
    {
        var servicios = new ServiciosComerciales(Path.Combine(Path.GetTempPath(), $"ventas-concurrencia-{Guid.NewGuid():N}.db"));
        const string numeroA = "T-000001";
        const string numeroB = "T-000002";
        var creada = DateTimeOffset.UtcNow.ToString("O");
        var resumenA = new ResumenVenta("venta-a", numeroA, creada, 10000, "confirmada", null, false, 1);
        var resumenB = new ResumenVenta("venta-b", numeroB, creada, 10000, "confirmada", null, false, 1);
        var inicioA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inicioB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var respuestaA = new TaskCompletionSource<IReadOnlyList<ResumenVenta>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var respuestaB = new TaskCompletionSource<IReadOnlyList<ResumenVenta>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new VentasConsultaViewModel(servicios, () => null, null,
            (texto, _, _, _, _, _, _, _) =>
            {
                if (texto == "A") { inicioA.TrySetResult(); return respuestaA.Task; }
                inicioB.TrySetResult();
                return respuestaB.Task;
            }, (_, _) => Task.CompletedTask);

        vm.TextoBusqueda = "A";
        var tareaA = vm.EsperarBusquedaAsyncPublic();
        Assert.False(tareaA.IsCompleted);
        await inicioA.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.TextoBusqueda = "B";
        var tareaB = vm.EsperarBusquedaAsyncPublic();
        await inicioB.Task.WaitAsync(TimeSpan.FromSeconds(2));
        respuestaB.SetResult([resumenB]);
        await tareaB;
        respuestaA.SetResult([resumenA]);
        await tareaA;

        Assert.Equal(numeroB, Assert.Single(vm.Historial).Resumen.Numero);
    }

    [Fact]
    public async Task Refresco_de_datos_conserva_la_venta_seleccionada_si_sigue_en_la_pagina()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        await e.VenderAsync(1, 10000);
        await e.VenderAsync(1, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsyncPublic();
        var seleccion = vm.Historial[1];
        vm.FilaSeleccionada = seleccion;
        await vm.CargarFichaSeleccionadaAsyncPublic();

        await vm.RefrescarAsyncPublic();

        Assert.Equal(seleccion.Resumen.Id, vm.FilaSeleccionada!.Resumen.Id);
        Assert.Equal(seleccion.Resumen.Id, vm.Ficha!.Venta.Id);
    }

    [Fact]
    public async Task Paginacion_conserva_anterior_siguiente_y_numero_de_pagina()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        await e.VenderSerieAsync(21, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        using var coordinador = new CoordinadorActualizacionVistas(e.Servicios.NotificadorCambios);
        coordinador.Registrar(vm, [AreaDatos.Ventas, AreaDatos.Cambios], vm.CargarAsyncPublic,
            () => !vm.Ocupado && vm.FilaSeleccionada is null && !vm.ConfirmacionVisible,
            () => vm.Error is not null, iniciarDesactualizada: true);

        await coordinador.ActivarAsync(vm);
        Assert.Equal(20, vm.Historial.Count);
        Assert.True(vm.HaySiguiente);
        Assert.False(vm.HayAnterior);
        Assert.Equal("Página 1", vm.PaginaTexto);

        await vm.SiguienteAsyncPublic();
        Assert.Single(vm.Historial);
        Assert.False(vm.HaySiguiente);
        Assert.True(vm.HayAnterior);
        Assert.Equal("Página 2", vm.PaginaTexto);

        await vm.AnteriorAsyncPublic();
        Assert.Equal(20, vm.Historial.Count);
        Assert.True(vm.HaySiguiente);
        Assert.False(vm.HayAnterior);
        Assert.Equal("Página 1", vm.PaginaTexto);
    }

    [Fact]
    public async Task Anular_pide_motivo_y_actualiza_fila()
    {
        using var e = await EntornoVentasUi.CrearAsync();
        var numero = await e.VenderAsync(1, 10000);
        var vm = new VentasConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsyncPublic();
        vm.FilaSeleccionada = vm.Historial[0];
        await vm.CargarFichaSeleccionadaAsyncPublic();

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
        await vm.CargarFichaSeleccionadaAsyncPublic();
        await vm.VerTicketAsyncPublic();
        Assert.NotNull(e.Dialogos.UltimoTicket);
    }

    private sealed class DialogosVentasFake : IDialogosVentas
    {
        public string? UltimoTicket { get; private set; }
        public void MostrarTicket(string ventaId) => UltimoTicket = ventaId;
        public bool ConfirmarCobroManualDuplicado(string detalle) => true;
    }

    private sealed class EntornoVentasUi : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public DialogosVentasFake Dialogos { get; } = new();
        public string Ruta { get; }
        private readonly string _dir;

        private EntornoVentasUi(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            Ruta = Path.Combine(dir, "ventas-ui-f04.db");
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

            await servicios.CasoAbrirCaja.EjecutarAsync(0, $"op-apertura-test-{Guid.NewGuid():N}", sesion.IdSesion);

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

        public async Task VenderSerieAsync(int cantidad, long precio)
        {
            var prod = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera Paginacion", null, "cat-rem"), Sesion);
            var variante = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), Sesion);
            await Servicios.CasoFijarPrecio.EjecutarAsync(variante, precio, Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(variante, cantidad + 1) }, Guid.NewGuid().ToString("N"), Sesion);

            for (var i = 0; i < cantidad; i++)
            {
                await Servicios.CasoConfirmarVenta.EjecutarAsync(
                    new[] { new NuevaLineaVenta(variante, 1, precio) },
                    new[] { new NuevoPago(MediosPago.Efectivo, precio, precio) },
                    Guid.NewGuid().ToString("N"),
                    Sesion);
            }
        }

        public async Task FijarFechaAsync(string numero, DateTime fechaLocal)
        {
            // La venta se confirmó con el reloj real: si se le asigna una
            // creada_en posterior a ese instante, el CHECK de la base exigue
            // que confirmada_en quede acorde. max() con confirmada en NULL
            // devuelve NULL en SQLite y una venta pendiente sigue siéndolo.
            var ahora = DateTime.Now;
            var confirmadaLocal = fechaLocal > ahora ? fechaLocal : ahora;
            var utc = new DateTimeOffset(DateTime.SpecifyKind(fechaLocal, DateTimeKind.Local)).ToUniversalTime().ToString("O");
            var utcConfirmada = new DateTimeOffset(DateTime.SpecifyKind(confirmadaLocal, DateTimeKind.Local)).ToUniversalTime().ToString("O");
            await using var conexion = new SqliteConnection($"Data Source={Ruta};Pooling=False");
            await conexion.OpenAsync();
            await using var comando = conexion.CreateCommand();
            comando.CommandText = "UPDATE venta SET creada_en = $fecha, confirmada_en = max(confirmada_en, $confirmada) WHERE numero = $numero;";
            comando.Parameters.AddWithValue("$fecha", utc);
            comando.Parameters.AddWithValue("$confirmada", utcConfirmada);
            comando.Parameters.AddWithValue("$numero", numero);
            Assert.Equal(1, await comando.ExecuteNonQueryAsync());
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
