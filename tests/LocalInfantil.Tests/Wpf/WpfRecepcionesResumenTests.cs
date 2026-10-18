using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;
using Microsoft.Data.Sqlite;
using System.Windows.Threading;
using System.Collections.Concurrent;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// E3 Recepciones: resumen batch en una lectura, filtros, peek, detalle con
/// diálogos fake y verificación de filas de recepción.
/// Las operaciones del VM se aguardan por su propia finalización (await sobre el
/// Task devuelto): cada comando SQLite ya está acotado por su busy_timeout
/// interno, así que un tope de reloj externo no aporta protección y, al valer lo
/// mismo que ese límite interno, fallaba bajo la carga de la suite completa.
/// </summary>
public sealed class WpfRecepcionesResumenTests
{
    [Fact]
    public async Task Buscar_desde_pagina_tres_reinicia_offset_y_busqueda_filtrada_pagina()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov paginación nueva");
        var (variante, _, _, _) = await e.CrearVariantesConStockAsync("PGN", 50, 50);
        for (var i = 1; i <= 5; i++)
            await e.ConfirmarAsync(prov, [new LineaRecepcion(variante, 1, null, null)], $"GRUPO-{i:00}");

        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion) { TamanoPagina = 2 };
        await vm.CargarAsync();
        await vm.SiguienteAsync();
        await vm.SiguienteAsync();
        Assert.Equal(4, vm.Desplazamiento);
        Assert.Equal("Página 3", vm.PaginaTexto);

        vm.TextoBusqueda = "GRUPO-04";
        await vm.BuscarAsyncPublic();

        Assert.Equal(0, vm.Desplazamiento);
        Assert.Equal("Página 1", vm.PaginaTexto);
        Assert.Single(vm.Historial);
        Assert.Equal("GRUPO-04", vm.Historial[0].Referencia);
        Assert.False(vm.HayAnterior);
        Assert.False(vm.HaySiguiente);

        // La navegación conserva el criterio aplicado, no un texto de edición.
        vm.TextoBusqueda = "texto todavía sin aplicar";
        vm.TextoBusqueda = "GRUPO";
        await vm.BuscarAsyncPublic();
        Assert.Equal(2, vm.Historial.Count); // 2 filas visibles + peek para HaySiguiente.
        Assert.True(vm.HaySiguiente);
        vm.TextoBusqueda = "texto todavía sin aplicar";
        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);
        Assert.Equal(2, vm.Historial.Count);
        Assert.All(vm.Historial, f => Assert.StartsWith("GRUPO-", f.Referencia));
        await vm.SiguienteAsync();
        Assert.Equal(4, vm.Desplazamiento);
        Assert.All(vm.Historial, f => Assert.StartsWith("GRUPO-", f.Referencia));
        vm.TextoBusqueda = "GRUPO-03";
        await vm.BuscarAsyncPublic();
        Assert.Equal(0, vm.Desplazamiento);
        Assert.Single(vm.Historial);
        Assert.Equal("GRUPO-03", vm.Historial[0].Referencia);
    }

    [Fact]
    public async Task Limpiar_busqueda_recarga_primera_pagina_y_conserva_filtro_estado()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov limpiar búsqueda");
        var (variante, _, _, _) = await e.CrearVariantesConStockAsync("CLR", 30, 30);
        var revertida = await e.ConfirmarAsync(prov, [new LineaRecepcion(variante, 1, null, null)], "CLARO-01");
        await e.ConfirmarAsync(prov, [new LineaRecepcion(variante, 1, null, null)], "CLARO-02");
        await e.ConfirmarAsync(prov, [new LineaRecepcion(variante, 1, null, null)], "CLARO-03");
        await e.ConfirmarAsync(prov, [new LineaRecepcion(variante, 1, null, null)], "CLARO-04");
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(revertida, "motivo de prueba", Guid.NewGuid().ToString("N"), e.Sesion);

        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion) { TamanoPagina = 2 };
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Historial[0];
        await EsperarAsync(() => vm.LineasDetalle.Count == 1);
        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);

        vm.TextoBusqueda = "NO-EXISTE";
        await vm.BuscarAsyncPublic();
        Assert.Empty(vm.Historial);
        Assert.Null(vm.FilaSeleccionada);
        Assert.Null(vm.Seleccionada);
        Assert.Empty(vm.LineasDetalle);
        Assert.Equal("Página 1", vm.PaginaTexto);
        Assert.False(vm.HayAnterior);
        Assert.False(vm.HaySiguiente);

        // Borrar por completo dispara la nueva consulta sin pulsar Buscar.
        vm.TextoBusqueda = "   ";
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 2);
        Assert.Equal(0, vm.Desplazamiento);
        Assert.Equal("Página 1", vm.PaginaTexto);
        Assert.Equal(2, vm.Historial.Count);

        vm.FiltroEstado = FiltroRecepciones.Confirmadas;
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 2);
        vm.TextoBusqueda = "CLARO-02";
        await vm.BuscarAsyncPublic();
        Assert.Single(vm.Historial);
        vm.TextoBusqueda = "   ";
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 2);
        Assert.Equal(0, vm.Desplazamiento);
        Assert.All(vm.Historial, f => Assert.False(f.Revertida));

        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);
        vm.TamanoPagina = 20;
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 3);
        Assert.Equal(0, vm.Desplazamiento);
        Assert.All(vm.Historial, f => Assert.False(f.Revertida));
        Assert.False(vm.HayAnterior);
        Assert.False(vm.HaySiguiente);

        vm.TamanoPagina = 2;
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 2);
        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);
        await vm.BuscarAsyncPublic(); // Buscar vacío desde página 2 también vuelve a la página inicial.
        Assert.Equal(0, vm.Desplazamiento);
        Assert.Equal("Página 1", vm.PaginaTexto);
        Assert.Equal(2, vm.Historial.Count);
    }

    [Fact]
    public async Task Consulta_fechas_desde_hasta_rango_igual_y_sin_resultados()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Proveedor fechas");
        var (variante, _, _, _) = await e.CrearVariantesConStockAsync("FEC", 20, 20);
        var fecha = new DateOnly(2026, 10, 15);
        var inicio = InicioDiaLocalUtc(fecha);
        var finExclusivo = InicioDiaLocalUtc(fecha.AddDays(1));

        await CrearConFechaAsync(e, prov, variante, "ANTES", inicio.AddSeconds(-1), TimeSpan.FromHours(2));
        await CrearConFechaAsync(e, prov, variante, "INICIO", inicio, TimeSpan.FromHours(5));
        await CrearConFechaAsync(e, prov, variante, "MEDIO", inicio.AddHours(12), TimeSpan.FromHours(-4));
        await CrearConFechaAsync(e, prov, variante, "FINAL", finExclusivo.AddMilliseconds(-10), TimeSpan.FromHours(3));
        await CrearConFechaAsync(e, prov, variante, "DESPUES", finExclusivo, TimeSpan.FromHours(-5));

        var desde = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion, fecha, null);
        Assert.Equal(new[] { "DESPUES", "FINAL", "INICIO", "MEDIO" }, desde.Select(r => r.ComprobanteRef).Order());

        var hasta = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion, null, fecha);
        Assert.Equal(new[] { "ANTES", "FINAL", "INICIO", "MEDIO" }, hasta.Select(r => r.ComprobanteRef).Order());

        var rango = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion, fecha, fecha);
        Assert.Equal(3, rango.Count);
        Assert.Equal(new[] { "FINAL", "INICIO", "MEDIO" }, rango.Select(r => r.ComprobanteRef).Order());

        var vacio = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion, fecha.AddDays(3), fecha.AddDays(4));
        Assert.Empty(vacio);
    }

    [Fact]
    public async Task Rango_fechas_se_combina_con_busqueda_estado_y_filtra_antes_de_paginar()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Proveedor filtro fecha");
        var (variante, _, _, _) = await e.CrearVariantesConStockAsync("FST", 30, 30);
        var fecha = new DateOnly(2026, 10, 15);
        var inicio = InicioDiaLocalUtc(fecha);
        var id1 = await CrearConFechaAsync(e, prov, variante, "FECHA-REV-1", inicio.AddHours(1), TimeSpan.FromHours(4));
        await CrearConFechaAsync(e, prov, variante, "FECHA-OK-1", inicio.AddHours(2), TimeSpan.FromHours(-3));
        var id2 = await CrearConFechaAsync(e, prov, variante, "FECHA-REV-2", inicio.AddHours(3), TimeSpan.FromHours(5));
        var id3 = await CrearConFechaAsync(e, prov, variante, "FECHA-REV-3", inicio.AddHours(4), TimeSpan.FromHours(-5));
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(id1, "prueba fechas", Guid.NewGuid().ToString("N"), e.Sesion);
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(id2, "prueba fechas", Guid.NewGuid().ToString("N"), e.Sesion);
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(id3, "prueba fechas", Guid.NewGuid().ToString("N"), e.Sesion);

        var pagina1 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("FECHA-REV", FiltroRecepciones.Revertidas, 2, 0, e.Sesion, fecha, fecha);
        Assert.Equal(3, pagina1.Count); // 2 visibles + peek: el filtro ocurrió antes de LIMIT/OFFSET.
        Assert.All(pagina1, r => Assert.True(r.Revertida));
        Assert.All(pagina1, r => Assert.Contains("FECHA-REV", r.ComprobanteRef));
        var pagina2 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("FECHA-REV", FiltroRecepciones.Revertidas, 2, 2, e.Sesion, fecha, fecha);
        Assert.Single(pagina2);
        Assert.Equal("FECHA-REV-1", pagina2[0].ComprobanteRef);
    }

    [Fact]
    public async Task ViewModel_aplica_rango_desde_pagina_dos_y_limpiar_fechas_restaura_resultados()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Proveedor VM fechas");
        var (variante, _, _, _) = await e.CrearVariantesConStockAsync("FVM", 30, 30);
        var fecha = new DateOnly(2026, 10, 15);
        var inicio = InicioDiaLocalUtc(fecha);
        for (var i = 1; i <= 5; i++)
        {
            var momento = i <= 3 ? inicio.AddHours(i) : inicio.AddDays(2).AddHours(i);
            await CrearConFechaAsync(e, prov, variante, $"VM-{i}", momento, TimeSpan.FromHours(i % 2 == 0 ? 4 : -3));
        }

        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion) { TamanoPagina = 2 };
        await vm.CargarAsync();
        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);

        vm.Desde = fecha.ToDateTime(TimeOnly.MinValue);
        vm.Hasta = fecha.ToDateTime(TimeOnly.MinValue);
        Assert.Equal(2, vm.Desplazamiento); // editar no cambia resultados hasta Buscar.
        await vm.BuscarAsyncPublic();
        Assert.Equal(0, vm.Desplazamiento);
        Assert.Equal(2, vm.Historial.Count);
        Assert.Equal("Página 1", vm.PaginaTexto);
        await vm.SiguienteAsync();
        Assert.Equal(2, vm.Desplazamiento);
        Assert.Single(vm.Historial);
        Assert.StartsWith("VM-", vm.Historial[0].Referencia);

        vm.FiltroEstado = FiltroRecepciones.Confirmadas;
        await EsperarAsync(() => !vm.Ocupado && vm.Desplazamiento == 0 && vm.Historial.Count == 2);
        vm.TextoBusqueda = "VM-";
        await vm.BuscarAsyncPublic();
        Assert.Equal(2, vm.Historial.Count);
        Assert.All(vm.Historial, fila => Assert.False(fila.Revertida));

        await vm.LimpiarFechasAsyncPublic();
        Assert.Null(vm.Desde);
        Assert.Null(vm.Hasta);
        Assert.Equal(0, vm.Desplazamiento);
        Assert.Equal(2, vm.Historial.Count); // permanece el texto VM- y el estado Confirmadas.
        Assert.True(vm.HaySiguiente);
        Assert.All(vm.Historial, fila => Assert.False(fila.Revertida));
    }

    [Fact]
    public async Task ViewModel_rechaza_rango_invertido_sin_consultar()
    {
        using var e = await Entorno.CrearAsync();
        var llamadas = 0;
        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion, null,
            (_, _, _, _, _, _, _, _) =>
            {
                llamadas++;
                return Task.FromResult<IReadOnlyList<ResumenRecepcion>>([]);
            });
        vm.Desde = new DateTime(2026, 10, 16);
        vm.Hasta = new DateTime(2026, 10, 15);

        await vm.BuscarAsyncPublic();

        Assert.Equal(0, llamadas);
        Assert.False(vm.Ocupado);
        Assert.Equal("La fecha Desde no puede ser posterior a la fecha Hasta.", vm.Error);
    }

    [Fact]
    public async Task Consulta_vieja_cancelada_que_finaliza_tarde_no_pisa_la_actual_y_error_libera_busy()
    {
        using var e = await Entorno.CrearAsync();
        var vieja = new TaskCompletionSource<IReadOnlyList<ResumenRecepcion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actual = new TaskCompletionSource<IReadOnlyList<ResumenRecepcion>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consultaActualIniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken tokenViejo = default;
        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion, null,
            (texto, _, _, _, _, _, _, token) =>
            {
                if (texto == "VIEJA")
                {
                    tokenViejo = token;
                    return vieja.Task; // Simula una respuesta que ignora cancelación.
                }
                if (texto.Length == 0)
                {
                    consultaActualIniciada.TrySetResult();
                    return actual.Task;
                }
                return Task.FromException<IReadOnlyList<ResumenRecepcion>>(new InvalidOperationException("Error de consulta simulado."));
            });

        vm.TextoBusqueda = "VIEJA";
        var cargaVieja = vm.BuscarAsyncPublic();
        Assert.True(vm.Ocupado);

        vm.TextoBusqueda = string.Empty;
        await consultaActualIniciada.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(tokenViejo.IsCancellationRequested);
        var recepcionActual = Resumen("actual", "ACTUAL-01");
        actual.SetResult([recepcionActual]);
        await EsperarAsync(() => !vm.Ocupado && vm.Historial.Count == 1);
        Assert.Equal("ACTUAL-01", vm.Historial[0].Referencia);

        vieja.SetResult([Resumen("vieja", "VIEJA-01")]);
        await cargaVieja;
        Assert.Single(vm.Historial);
        Assert.Equal("ACTUAL-01", vm.Historial[0].Referencia);
        Assert.Null(vm.Error);

        vm.TextoBusqueda = "ERROR";
        await vm.BuscarAsyncPublic();
        Assert.False(vm.Ocupado);
        Assert.Equal("Error de consulta simulado.", vm.Error);
    }

    private static ResumenRecepcion Resumen(string id, string comprobante)
        => new(id, "prov", "Proveedor", $"op-{id}", comprobante, null, null,
            "2026-10-17T12:00:00Z", 0, 0, 0, 0, false);

    private static DateTimeOffset InicioDiaLocalUtc(DateOnly fecha)
    {
        var inicioLocal = DateTime.SpecifyKind(fecha.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(inicioLocal, TimeZoneInfo.Local), TimeSpan.Zero);
    }

    private static async Task<string> CrearConFechaAsync(Entorno e, string proveedorId, string varianteId, string comprobante, DateTimeOffset instanteUtc, TimeSpan offsetPersistido)
    {
        var id = await e.ConfirmarAsync(proveedorId, [new LineaRecepcion(varianteId, 1, null, null)], comprobante);
        using var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = e.RutaBaseDatos,
            Pooling = false,
        }.ToString());
        await conexion.OpenAsync();
        using var comando = conexion.CreateCommand();
        comando.CommandText = "UPDATE recepcion SET creado_en = $fecha WHERE id = $id;";
        comando.Parameters.AddWithValue("$fecha", instanteUtc.ToOffset(offsetPersistido).ToString("o"));
        comando.Parameters.AddWithValue("$id", id);
        Assert.Equal(1, await comando.ExecuteNonQueryAsync());
        return id;
    }

    [Fact]
    public async Task Resumen_batch_con_agregados_y_total_solo_si_hay_costos()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Resumen");
        var (v1, v2, _, _) = await e.CrearVariantesConStockAsync("RSM", 5, 7);
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 5, 1000, "ARS"), new LineaRecepcion(v2, 7, null, null) }, "COMP-R1");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 2, 2000, "ARS") }, null);

        var lista = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 20, 0, e.Sesion);
        Assert.Equal(2, lista.Count);
        var conComp = lista.First(r => r.ComprobanteRef == "COMP-R1");
        Assert.Equal("Prov Resumen", conComp.ProveedorNombre);
        Assert.Equal(2, conComp.CantidadLineas);
        Assert.Equal(12, conComp.Unidades);
        Assert.Equal(1, conComp.LineasSinCosto);
        Assert.False(conComp.Revertida);
        Assert.Equal("duena", conComp.CreadoPorNombre);
        var sinComp = lista.First(r => r.ComprobanteRef is null);
        Assert.Equal(0, sinComp.LineasSinCosto);
        Assert.Equal(4000, sinComp.TotalMinor);
        // Ambos estados provienen de datos guardados en esta base temporal:
        // el comprobante externo se conserva y nunca se suplanta por un ID.
        Assert.True(new FilaHistorialRecepcion(conComp).ComprobanteInformado);
        Assert.Equal("COMP-R1", new FilaHistorialRecepcion(conComp).Referencia);
        Assert.False(new FilaHistorialRecepcion(sinComp).ComprobanteInformado);
        Assert.Equal("No informado", new FilaHistorialRecepcion(sinComp).Referencia);
        Assert.Equal("COMP-R1", new DetalleRecepcionViewModel(e.Servicios, () => e.Sesion, conComp).ComprobanteTexto);
        Assert.Equal("No informado", new DetalleRecepcionViewModel(e.Servicios, () => e.Sesion, sinComp).ComprobanteTexto);
    }

    [Fact]
    public async Task Filtro_revertidas_y_peek_exacto()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Filtro");
        var (v1, _, _, _) = await e.CrearVariantesConStockAsync("FLT", 10, 10);
        var id1 = await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-1");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-2");
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 1, null, null) }, "C-3");
        await e.Servicios.CasoRevertirRecepcion.EjecutarAsync(id1, "motivo", Guid.NewGuid().ToString("N"), e.Sesion);

        var rev = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Revertidas, 20, 0, e.Sesion);
        Assert.Single(rev);
        Assert.True(rev[0].Revertida);
        var conf = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Confirmadas, 20, 0, e.Sesion);
        Assert.Equal(2, conf.Count);

        var pag1 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 2, 0, e.Sesion);
        Assert.Equal(3, pag1.Count); // 2 + peek
        var pag2 = await e.Servicios.ConsultasRecepciones.BuscarResumenAsync("", FiltroRecepciones.Todas, 2, 2, e.Sesion);
        Assert.Single(pag2);
    }

    [Fact]
    public async Task Lineas_detalle_resuelven_sku_en_una_lectura()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Detalle");
        var (v1, v2, cod1, cod2) = await e.CrearVariantesConStockAsync("DET", 3, 4);
        var id = await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 3, 500, "ARS"), new LineaRecepcion(v2, 4, null, null) }, "C-D");
        var lineas = await e.Servicios.ConsultasRecepciones.ListarLineasDetalleAsync(id, e.Sesion);
        Assert.Equal(2, lineas.Count);
        Assert.Equal(new[] { cod1, cod2 }.OrderBy(c => c), lineas.Select(l => l.Sku).OrderBy(c => c));
        Assert.Equal(1500, lineas.First(l => l.Sku == cod1).SubtotalMinor);
        Assert.Null(lineas.First(l => l.Sku == cod2).SubtotalMinor);
    }

    [Fact]
    public async Task Consulta_vm_historial_detalle_y_dialogo()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov VM");
        var (v1, _, _, _) = await e.CrearVariantesConStockAsync("CVM", 6, 6);
        await e.ConfirmarAsync(prov, new[] { new LineaRecepcion(v1, 6, 700, "ARS") }, "C-VM");
        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsync();
        Assert.Single(vm.Historial);
        var fila = vm.Historial[0];
        Assert.Equal("C-VM", fila.Referencia);
        Assert.Equal("Prov VM", fila.Resumen.ProveedorNombre);
        Assert.Equal("Confirmada", fila.EstadoTexto);

        vm.FilaSeleccionada = fila;
        await EsperarAsync(() => vm.LineasDetalle.Count == 1);
        Assert.Equal(6, vm.UnidadesDetalle);
        Assert.Equal(4200, vm.TotalDetalleExacto);
        Assert.Equal("duena", vm.ResponsableDetalle);

        await vm.AbrirDetalleAsyncPublic();
        Assert.Equal(fila.Resumen.Id, e.Dialogos.UltimoId);

        vm.FilaSeleccionada = null;
        await vm.AbrirDetalleAsyncPublic();
        Assert.Contains("Seleccioná", vm.Error ?? "");
    }

    [Fact]
    public async Task Al_deseleccionar_o_recargar_el_historial_se_limpia_el_detalle_anterior()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov sin selección");
        var (v1, _, _, _) = await e.CrearVariantesConStockAsync("SEL", 3, 3);
        await e.ConfirmarAsync(prov, [new LineaRecepcion(v1, 2, 500, "ARS")], "REF-1");

        var vm = new RecepcionesConsultaViewModel(e.Servicios, () => e.Sesion, e.Dialogos);
        await vm.CargarAsync();
        vm.FilaSeleccionada = Assert.Single(vm.Historial);
        await EsperarAsync(() => vm.LineasDetalle.Count == 1);
        Assert.NotNull(vm.Seleccionada);
        Assert.Equal(2, vm.UnidadesDetalle);

        vm.FilaSeleccionada = null;

        Assert.Null(vm.Seleccionada);
        Assert.Empty(vm.LineasDetalle);
        Assert.Equal(0, vm.UnidadesDetalle);
        Assert.Null(vm.TotalDetalleExacto);
        Assert.False(vm.ConfirmacionVisible);

        await vm.CargarAsync();

        Assert.Null(vm.FilaSeleccionada);
        Assert.Null(vm.Seleccionada);
        Assert.Empty(vm.LineasDetalle);
    }

    [Fact]
    public async Task Entrada_verificar_expone_stock_quedara_y_resumen()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov Entrada");
        var (v1, _, cod1, _) = await e.CrearVariantesConStockAsync("ENT", 10, 10);
        var rec = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        await rec.CargarProveedoresAsync();
        rec.ProveedorSeleccionado = rec.Proveedores.First(p => p.Id == prov);
        rec.Filas[0].Sku = cod1;
        rec.Filas[0].CantidadTexto = "4";
        rec.Filas[0].CostoTexto = "100,00";
        rec.ComprobanteRef = "COMP-RECEPCION-1";
        Assert.False(rec.PuedeConfirmar);
        await rec.VerificarAsyncPublic();
        var fila = rec.Filas[0];
        Assert.True(fila.Resuelta);
        Assert.True(fila.Valida);
        Assert.Equal(10, fila.StockActual);
        Assert.Equal(14, fila.Quedara);
        Assert.Equal(40000, fila.SubtotalMinor);
        Assert.Equal(1, rec.CantidadLineas);
        Assert.Equal(4, rec.UnidadesSumar);
        Assert.Equal(40000, rec.TotalEstimado);
        Assert.Contains("sumar 4 unidades", rec.ConfirmarTexto);
        Assert.True(rec.PuedeConfirmar);

        await rec.ConfirmarAsyncPublic();
        var variantePosterior = await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, e.Sesion);
        var fichaPosterior = await e.Servicios.ConsultasCatalogo.ObtenerFichaAsync(variantePosterior!.ProductoId, DateTime.UtcNow.ToString("o"), e.Sesion);
        Assert.Equal(14, fichaPosterior!.Variantes.Single(v => v.Id == v1).Existencias);
        Assert.Empty(rec.Filas);
        Assert.Null(rec.ProveedorSeleccionado);
        Assert.Equal(string.Empty, rec.ComprobanteRef);
        Assert.Equal(0, rec.CantidadLineas);
        Assert.Equal(0, rec.UnidadesSumar);
        Assert.Null(rec.TotalEstimado);
        Assert.Equal("Sin importes cargados", rec.CostoEstimadoTexto);
        Assert.False(rec.PuedeConfirmar);
        Assert.Contains("Se sumaron 4 unidades", rec.Mensaje);
        Assert.Null(rec.Error);
        Assert.True(rec.EtiquetasRecepcionDisponibles);
        var etiquetas = rec.CrearEtiquetasRecepcionViewModel();
        await etiquetas.InicializarAsync();
        var etiqueta = Assert.Single(etiquetas.Filas);
        Assert.Equal(v1, etiqueta.Variante.VarianteId);
        Assert.Equal(4, etiqueta.Cantidad);

        // La nueva recepción usa otra identidad y no repite la ya confirmada.
        var operacionAnterior = rec.OperacionId;
        rec.ProveedorSeleccionado = rec.Proveedores.Single(p => p.Id == prov);
        rec.Filas.Add(new FilaRecepcionViewModel { Sku = cod1, CantidadTexto = "2" });
        await rec.ConfirmarAsyncPublic();
        Assert.NotEqual(operacionAnterior, rec.OperacionId);
        Assert.Equal(16, (await e.Servicios.ConsultasCatalogo.ObtenerFichaAsync(
            (await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, e.Sesion))!.ProductoId,
            DateTime.UtcNow.ToString("o"), e.Sesion))!.Variantes.Single(v => v.Id == v1).Existencias);
        Assert.Equal(2, (await e.Servicios.ConsultasRecepciones.ListarAsync(10, 0, e.Sesion)).Count);
    }

    [Fact]
    public async Task Doble_confirmacion_concurrente_no_duplica_stock_y_error_conserva_borrador()
    {
        using var e = await Entorno.CrearAsync();
        var prov = await e.CrearProveedorAsync("Prov doble clic");
        var (v1, _, cod1, _) = await e.CrearVariantesConStockAsync("DOBLE", 1, 1);
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.Single(p => p.Id == prov);
        vm.ComprobanteRef = "DOBLE-1";
        vm.Filas.Add(new FilaRecepcionViewModel { Sku = cod1, CantidadTexto = "3", CostoTexto = "25,00" });
        var primera = vm.ConfirmarAsyncPublic();
        var segunda = vm.ConfirmarAsyncPublic();
        await Task.WhenAll(primera, segunda);

        var variante = await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, e.Sesion);
        var ficha = await e.Servicios.ConsultasCatalogo.ObtenerFichaAsync(variante!.ProductoId, DateTime.UtcNow.ToString("o"), e.Sesion);
        Assert.Equal(4, ficha!.Variantes.Single(x => x.Id == v1).Existencias);
        Assert.Single(await e.Servicios.ConsultasRecepciones.ListarAsync(10, 0, e.Sesion));
        Assert.Empty(vm.Filas);

        var vmError = new RecepcionViewModel(e.Servicios, () => null)
        {
            ProveedorSeleccionado = new ProveedorRegistrado(prov, "Prov doble clic", true),
            ComprobanteRef = "CONSERVAR-ERROR"
        };
        vmError.Filas.Add(new FilaRecepcionViewModel { Sku = cod1, CantidadTexto = "7", CostoTexto = "99,00" });
        var operacion = vmError.OperacionId;
        await vmError.ConfirmarAsyncPublic();
        Assert.NotNull(vmError.Error);
        Assert.Equal(operacion, vmError.OperacionId);
        Assert.Equal("CONSERVAR-ERROR", vmError.ComprobanteRef);
        Assert.Equal(prov, vmError.ProveedorSeleccionado?.Id);
        Assert.Contains(vmError.Filas, fila => fila.Sku == cod1 && fila.CantidadTexto == "7" && fila.CostoTexto == "99,00");
    }

    [Fact]
    public async Task Verificacion_de_dos_variantes_calcula_stock_y_costo_sin_inventar_costos()
    {
        using var e = await Entorno.CrearAsync();
        var (_, _, cod1, cod2) = await e.CrearVariantesConStockAsync("PAR", 10, 7);
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        vm.Filas[0].Sku = cod1;
        vm.Filas[0].CantidadTexto = "4";
        vm.Filas[0].CostoTexto = "100,00";
        vm.Filas[1].Sku = cod2;
        vm.Filas[1].CantidadTexto = "3";

        await vm.VerificarAsyncPublic();

        Assert.False(vm.Ocupado);
        Assert.Equal(2, vm.CantidadLineas);
        Assert.Equal(10, vm.Filas[0].StockActual);
        Assert.Equal(14, vm.Filas[0].Quedara);
        Assert.Equal(40_000, vm.Filas[0].SubtotalMinor);
        Assert.Equal(7, vm.Filas[1].StockActual);
        Assert.Equal(10, vm.Filas[1].Quedara);
        Assert.Null(vm.Filas[1].SubtotalMinor);
        Assert.Null(vm.TotalEstimado);

        // Verificar de nuevo actualiza las mismas filas; no agrega copias.
        await vm.VerificarAsyncPublic();
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(new[] { cod1, cod2 }, vm.Filas.Select(f => f.Sku));
    }

    [Fact]
    public async Task Resumen_de_recepcion_se_actualiza_al_editar_y_agregar_o_quitar_filas_sin_verificar()
    {
        using var e = await Entorno.CrearAsync();
        var (v1, v2, codigo1, codigo2) = await e.CrearVariantesConStockAsync("LIVE", 5, 7);
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        var primera = vm.Filas[0];
        var segunda = vm.Filas[1];

        primera.Sku = codigo1;
        primera.VarianteId = v1;
        primera.CantidadTexto = "5";
        primera.CostoTexto = "1000";
        segunda.Sku = codigo2;
        segunda.VarianteId = v2;
        segunda.CantidadTexto = "3";
        segunda.CostoTexto = "2000";

        Assert.Equal(2, vm.CantidadLineas);
        Assert.Equal(8, vm.UnidadesSumar);
        Assert.Equal(1_100_000, vm.TotalEstimado);
        Assert.Equal("ARS 11.000,00", vm.CostoEstimadoTexto);
        Assert.Equal("Confirmar y sumar 8 unidades", vm.ConfirmarTexto);

        segunda.CantidadTexto = "4";
        Assert.Equal(9, vm.UnidadesSumar);
        Assert.Equal(1_300_000, vm.TotalEstimado);
        Assert.Equal("Confirmar y sumar 9 unidades", vm.ConfirmarTexto);

        segunda.CostoTexto = string.Empty;
        Assert.Null(vm.TotalEstimado);
        Assert.Contains("conocidos", vm.CostoEstimadoTexto);

        segunda.CostoTexto = "2000";
        var nueva = new FilaRecepcionViewModel { Sku = "pendiente" };
        vm.Filas.Add(nueva);
        Assert.True(vm.TieneFilasIncompletas);
        Assert.Null(vm.TotalEstimado);

        nueva.CantidadTexto = "inválida";
        Assert.True(vm.TieneFilasIncompletas);
        vm.Filas.Remove(nueva);
        Assert.False(vm.TieneFilasIncompletas);
        Assert.Equal(9, vm.UnidadesSumar);
        Assert.Equal(1_300_000, vm.TotalEstimado);

        segunda.Moneda = "USD";
        Assert.True(vm.TieneFilasIncompletas);
        Assert.Null(vm.TotalEstimado);
        Assert.Equal(5, vm.UnidadesSumar);
    }

    [Fact]
    public void Previews_de_stock_y_costo_se_actualizan_en_tiempo_real_con_aritmetica_exacta()
    {
        var fila = new FilaRecepcionViewModel
        {
            StockActual = 12,
            CantidadTexto = "3",
            CostoTexto = "1.234,50",
            Moneda = "ARS",
        };

        Assert.Equal(15, fila.Quedara);
        Assert.Equal(370_350, fila.SubtotalMinor);

        fila.CantidadTexto = "4";
        Assert.Equal(16, fila.Quedara);
        Assert.Equal(493_800, fila.SubtotalMinor);

        fila.CostoTexto = string.Empty;
        Assert.Equal(16, fila.Quedara);
        Assert.Null(fila.SubtotalMinor); // desconocido, no costo cero

        fila.CostoTexto = "1.234,567";
        Assert.Null(fila.SubtotalMinor); // nunca redondea silenciosamente

        fila.CantidadTexto = "0";
        Assert.Null(fila.Quedara);
        Assert.Null(fila.SubtotalMinor);
    }

    [Fact]
    public void Variante_duplicada_no_muestra_stock_posterior_engañoso()
    {
        var fila = new FilaRecepcionViewModel
        {
            StockActual = 8,
            CantidadTexto = "2",
        };

        Assert.Equal(10, fila.Quedara);
        fila.TieneDuplicado = true;

        Assert.Null(fila.Quedara);
        Assert.Contains("repetida", fila.AvisoEntrada);
    }

    [Fact]
    public async Task Seleccionar_sugerencia_carga_stock_y_detalle_sin_modificar_existencias()
    {
        using var e = await Entorno.CrearAsync();
        var (varianteId, _, codigo, _) = await e.CrearVariantesConStockAsync("AUTO", 9, 2);
        var varianteAntes = await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, e.Sesion);
        var fichaAntes = await e.Servicios.ConsultasCatalogo.ObtenerFichaAsync(varianteAntes!.ProductoId, DateTime.UtcNow.ToString("o"), e.Sesion);
        var stockAntes = fichaAntes!.Variantes.Single(v => v.Id == varianteId).Existencias;
        var sugerencia = Assert.Single(await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion));
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        var fila = vm.Filas[0];
        fila.CantidadTexto = "5";

        Assert.True(fila.SeleccionarSugerenciaCommand.CanExecute(sugerencia));
        fila.SeleccionarSugerenciaCommand.Execute(sugerencia);
        await EsperarAsync(() => !fila.CargandoStock);
        Assert.False(fila.CargandoStock, "La consulta seleccionada debe finalizar y liberar el estado ocupado.");

        Assert.Equal(codigo, fila.Sku);
        Assert.Equal(varianteId, fila.VarianteId);
        Assert.Contains(codigo, fila.Detalle);
        Assert.Equal(stockAntes, fila.StockActual);
        Assert.Equal(stockAntes + 5, fila.Quedara);

        var varianteDespues = await e.Servicios.ConsultasCatalogo.ObtenerVarianteAsync(varianteId, e.Sesion);
        var fichaDespues = await e.Servicios.ConsultasCatalogo.ObtenerFichaAsync(varianteDespues!.ProductoId, DateTime.UtcNow.ToString("o"), e.Sesion);
        Assert.Equal(stockAntes, fichaDespues!.Variantes.Single(v => v.Id == varianteId).Existencias);
    }

    [Fact]
    public async Task Consulta_stock_fuera_de_orden_no_aplica_variante_anterior_y_cancelacion_no_limpia_la_nueva()
    {
        var fila = new FilaRecepcionViewModel { Sku = "VAR-A" };
        var respuestaA = new TaskCompletionSource<(string Id, long Stock)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var respuestaB = new TaskCompletionSource<(string Id, long Stock)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var iniciadaA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var iniciadaB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenA = CancellationToken.None;

        async Task ResolverAsync(string codigo, TaskCompletionSource<(string Id, long Stock)> respuesta, TaskCompletionSource iniciada)
        {
            var (version, token) = fila.ComenzarResolucion();
            if (codigo == "VAR-A") tokenA = token;
            iniciada.TrySetResult();
            try
            {
                // Simula una consulta que ya está en curso y completa aunque se cancele:
                // el guard de versión/código sigue siendo la autoridad al aplicar el resultado.
                var resultado = await respuesta.Task;
                if (!fila.ResolucionVigente(version, codigo)) return;
                fila.VarianteId = resultado.Id;
                fila.StockActual = resultado.Stock;
                fila.Detalle = codigo;
                fila.Resuelta = true;
            }
            finally
            {
                fila.FinalizarResolucion(version);
            }
        }

        var consultaA = ResolverAsync("VAR-A", respuestaA, iniciadaA);
        await iniciadaA.Task.WaitAsync(TimeSpan.FromSeconds(2));
        fila.Sku = "VAR-B";
        Assert.True(tokenA.IsCancellationRequested);

        var consultaB = ResolverAsync("VAR-B", respuestaB, iniciadaB);
        await iniciadaB.Task.WaitAsync(TimeSpan.FromSeconds(2));
        respuestaB.SetResult(("variante-b", 27));
        await consultaB.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("variante-b", fila.VarianteId);
        Assert.Equal(27, fila.StockActual);
        Assert.Equal("VAR-B", fila.Detalle);
        Assert.True(fila.Resuelta);
        Assert.False(fila.CargandoStock);

        respuestaA.SetResult(("variante-a", 11));
        await consultaA.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("variante-b", fila.VarianteId);
        Assert.Equal(27, fila.StockActual);
        Assert.Equal("VAR-B", fila.Detalle);
        Assert.True(fila.Resuelta);
        Assert.False(fila.CargandoStock);
    }

    [Fact]
    public async Task Variante_inexistente_y_error_de_sesion_muestran_error_y_liberan_busy()
    {
        using var e = await Entorno.CrearAsync();
        var (_, _, codigo, _) = await e.CrearVariantesConStockAsync("ERR", 5, 1);
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
        var fila = vm.Filas[0];
        fila.Sku = "CODIGO-INEXISTENTE";
        fila.CantidadTexto = "1";

        await vm.VerificarAsyncPublic();

        Assert.False(vm.Ocupado);
        Assert.False(fila.Resuelta);
        Assert.False(fila.Valida);
        Assert.Equal("Código interno no encontrado.", fila.Detalle);

        fila.Sku = codigo;
        var sesionInvalida = new RecepcionViewModel(e.Servicios, () => null);
        sesionInvalida.Filas[0].Sku = codigo;
        sesionInvalida.Filas[0].CantidadTexto = "1";
        await sesionInvalida.VerificarAsyncPublic();

        Assert.False(sesionInvalida.Ocupado);
        Assert.False(sesionInvalida.Filas[0].Resuelta);
        Assert.Contains("Sesión inválida", sesionInvalida.Filas[0].Detalle);

        var sugerencia = Assert.Single(await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(codigo, 10, e.Sesion));
        var errorEnSeleccion = new RecepcionViewModel(e.Servicios, () => null);
        errorEnSeleccion.Filas[0].SeleccionarSugerenciaCommand.Execute(sugerencia);
        await EsperarAsync(() => !errorEnSeleccion.Filas[0].CargandoStock);
        Assert.False(errorEnSeleccion.Filas[0].CargandoStock);
        Assert.Contains("Sesión inválida", errorEnSeleccion.Filas[0].ErrorResolucion);
    }

    [Fact]
    public async Task Selecciones_repetidas_actualizan_la_fila_y_no_agregan_variantes_duplicadas()
    {
        using var e = await Entorno.CrearAsync();
        var (_, _, cod1, cod2) = await e.CrearVariantesConStockAsync("SEL", 3, 4);
        var sugerencias = await e.Servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync("SEL", 10, e.Sesion);
        var primera = Assert.Single(sugerencias.Where(s => s.CodigoInterno == cod1));
        var segunda = Assert.Single(sugerencias.Where(s => s.CodigoInterno == cod2));
        var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);

        for (var i = 0; i < 4; i++) vm.Filas[0].AplicarSugerencia(primera);
        vm.Filas[0].AplicarSugerencia(segunda);
        vm.Filas[0].CantidadTexto = "1";
        vm.Filas[1].AplicarSugerencia(primera);
        vm.Filas[1].CantidadTexto = "1";
        await vm.VerificarAsyncPublic();

        Assert.False(vm.Ocupado);
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(cod2, vm.Filas[0].Sku);
        Assert.Equal(cod1, vm.Filas[1].Sku);
        Assert.Equal(new[] { segunda.VarianteId, primera.VarianteId }, vm.Filas.Select(f => f.VarianteId));
    }

    [Fact]
    public async Task Verificar_variante_no_bloquea_dispatcher_mientras_sqlite_espera_un_lock()
    {
        using var e = await Entorno.CrearAsync();
        var (_, _, codigo, _) = await e.CrearVariantesConStockAsync("UIB", 4, 1);

        using var bloqueo = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = e.RutaBaseDatos,
            Pooling = false,
        }.ToString());
        bloqueo.Open();
        using (var journal = bloqueo.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode = DELETE;";
            Assert.Equal("delete", (journal.ExecuteScalar() as string)?.ToLowerInvariant());
        }
        using (var tomarBloqueo = bloqueo.CreateCommand())
        {
            tomarBloqueo.CommandText = "BEGIN EXCLUSIVE;";
            tomarBloqueo.ExecuteNonQuery();
        }

        var dispatcherRespondio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verificacionFinalizo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorUi = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hiloUi = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var vm = new RecepcionViewModel(e.Servicios, () => e.Sesion);
                vm.Filas[0].Sku = codigo;
                vm.Filas[0].CantidadTexto = "1";
                var verificacion = vm.VerificarAsyncPublic();

                var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
                {
                    Interval = TimeSpan.FromMilliseconds(300),
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    dispatcherRespondio.TrySetResult();
                };
                timer.Start();

                _ = verificacion.ContinueWith(_ => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Dispatcher.Run();
                verificacionFinalizo.TrySetResult();
            }
            catch (Exception ex)
            {
                errorUi.TrySetResult(ex);
                verificacionFinalizo.TrySetResult();
            }
        }) { IsBackground = true, Name = "LocalInfantil-Recepcion-Dispatcher-Test" };
        hiloUi.SetApartmentState(ApartmentState.STA);
        hiloUi.Start();

        var respondioConBloqueoActivo = false;
        try
        {
            var respuesta = await Task.WhenAny(dispatcherRespondio.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            respondioConBloqueoActivo = respuesta == dispatcherRespondio.Task;
        }
        finally
        {
            using var liberar = bloqueo.CreateCommand();
            liberar.CommandText = "ROLLBACK;";
            liberar.ExecuteNonQuery();
        }

        await verificacionFinalizo.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var excepcionUi = errorUi.Task.IsCompleted ? await errorUi.Task : null;
        Assert.Null(excepcionUi);
        Assert.True(await Task.Run(() => hiloUi.Join(TimeSpan.FromSeconds(2))), "El dispatcher de prueba debe finalizar.");
        Assert.True(respondioConBloqueoActivo, "El dispatcher no atendió su temporizador mientras la consulta SQLite esperaba el bloqueo.");
    }

    [Fact]
    public void Referencia_usa_comprobante_y_nunca_id_tecnico()
    {
        var conComp = new ResumenRecepcion("id-largo-0123456789", "p", "Prov", "op", "COMP-9", null, null, "2026-09-24T00:00:00Z", 1, 1, 0, 100, false);
        Assert.Equal("COMP-9", FilaHistorialRecepcion.ReferenciaDe(conComp));
        var sinComp = conComp with { ComprobanteRef = null };
        Assert.Equal("No informado", FilaHistorialRecepcion.ReferenciaDe(sinComp));
        Assert.DoesNotContain("id-largo", FilaHistorialRecepcion.ReferenciaDe(sinComp));
        Assert.Contains("24/09", FilaHistorialRecepcion.FechaCorta("2026-09-24T10:30:00Z"));
    }

    private static async Task EsperarAsync(Func<bool> listo, int intentos = 100)
    {
        for (var t = 0; t < intentos && !listo(); t++) await Task.Delay(50);
    }

    private sealed class DialogosFake : IDialogosRecepciones
    {
        public string? UltimoId { get; private set; }
        public void MostrarDetalleRecepcion(ResumenRecepcion resumen) => UltimoId = resumen.Id;
    }

    private sealed class Entorno : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        public string DuenaId { get; }
        public string RutaBaseDatos => Path.Combine(_dir, "recep.db");
        public DialogosFake Dialogos { get; } = new();
        private readonly string _dir;

        private Entorno(string dir, ServiciosComerciales servicios, string sesion, string duenaId)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            DuenaId = duenaId;
        }

        public Task<string> CrearProveedorAsync(string nombre)
            => Servicios.CasoCrearProveedor.EjecutarAsync(new NuevoProveedor(nombre), Sesion);

        public async Task<(string V1, string V2, string Cod1, string Cod2)> CrearVariantesConStockAsync(string prefijo, long s1, long s2)
        {
            var pid = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto($"Prod-{prefijo}", null, "cat-otr"), Sesion);
            var v1 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, "M", null), Sesion);
            var v2 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(pid, null, null), Sesion);
            await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(
                new[] { new FilaCargaInicial(v1, s1), new FilaCargaInicial(v2, s2) }, Guid.NewGuid().ToString("N"), Sesion);
            var cod1 = (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v1, Sesion))!.CodigoInterno;
            var cod2 = (await Servicios.ConsultasCatalogo.ObtenerVarianteAsync(v2, Sesion))!.CodigoInterno;
            return (v1, v2, cod1, cod2);
        }

        public async Task<string> ConfirmarAsync(string prov, IReadOnlyList<LineaRecepcion> lineas, string? comprobante)
            => await Servicios.CasoConfirmarRecepcion.EjecutarAsync(prov, lineas, Guid.NewGuid().ToString("N"), comprobante, Sesion);

        public static async Task<Entorno> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "recep.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var servicios = new ServiciosComerciales(ruta);
            var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            var e = new Entorno(dir, servicios, sesion.IdSesion, duenaId);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.ProveedoresGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.RecepcionConfirmar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCompras.RecepcionRevertir, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.ProductosGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosCatalogo.VariantesGestionar, true, e.Sesion);
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, PermisosInventario.CargaInicialEstablecer, true, e.Sesion);
            return e;
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
