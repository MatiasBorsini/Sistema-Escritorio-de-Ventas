using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Tests.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Clientes;

public sealed class ClientesTests
{
    [Fact]
    public async Task V13_a_V14_preserva_cliente_venta_caja_y_cambio_sin_crear_deuda_historica()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "v13-con-historicos.db");
        try
        {
            var migrador = new MigradorBaseDatos(path);
            await migrador.MigrarAsync(MigracionesF01.Todas.Take(13).ToArray());
            await using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await db.OpenAsync();
                using (var listar = db.CreateCommand())
                {
                    listar.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name IN ('caja_sesion','caja_movimiento','cambio');";
                    var triggers = new List<string>(); await using (var r = await listar.ExecuteReaderAsync()) while (await r.ReadAsync()) triggers.Add(r.GetString(0));
                    foreach (var trigger in triggers) { using var drop = db.CreateCommand(); drop.CommandText = $"DROP TRIGGER \"{trigger.Replace("\"", "\"\"")}\";"; await drop.ExecuteNonQueryAsync(); }
                }
                using var seed = db.CreateCommand();
                seed.CommandText = """
                    INSERT INTO usuario(id,nombre,rol,activo,creado_en,huella_contrasena) VALUES('usuario-historico','duena-historica','duena',1,'2026-01-01','hash-historico');
                    INSERT INTO cliente(id,nombre_completo,telefono,observacion,activo,creado_en,actualizado_en) VALUES('cliente-historico','Cliente histórico',NULL,NULL,1,'2026-01-01','2026-01-01');
                    INSERT INTO venta(id,numero,operacion_id,estado,total_minor,moneda,creada_en,confirmada_en,cliente_id,cliente_nombre_snapshot) VALUES('venta-historica','T-000041','op-vta-41','confirmada',12000,'ARS','2026-01-02','2026-01-02','cliente-historico','Cliente histórico');
                    INSERT INTO venta(id,numero,operacion_id,estado,total_minor,moneda,creada_en,confirmada_en) VALUES('venta-sin-cliente','T-000042','op-vta-42','confirmada',9000,'ARS','2026-01-02','2026-01-02');
                    INSERT INTO caja_sesion(id,usuario_id,estado,fondo_inicial_minor,moneda,abierta_en,operacion_apertura_id) VALUES('caja-historica','usuario-historico','abierta',5000,'ARS','2026-01-02','ap-historica');
                    INSERT INTO caja_operacion(id,operacion_id,sesion_id,tipo,usuario_id,creado_en) VALUES('caja-op-historica','op-caja-41','caja-historica','venta','usuario-historico','2026-01-02');
                    INSERT INTO caja_movimiento(id,sesion_id,tipo,importe_minor,venta_id,motivo,creado_en,caja_operacion_id,pago_id,medio,categoria,usuario_id,movimiento_origen_id) VALUES('caja-mov-historico','caja-historica','venta',12000,'venta-historica',NULL,'2026-01-02','caja-op-historica',NULL,'efectivo',NULL,'usuario-historico',NULL);
                    INSERT INTO cambio(id,numero,operacion_id,venta_id,usuario_id,autorizado_reintegro_por_usuario_id,creado_en,importe_entradas_minor,importe_salidas_minor,diferencia_minor,medio_reintegro,motivo) VALUES('cambio-historico','C-000004','op-cambio-4','venta-historica','usuario-historico',NULL,'2026-01-03',2000,0,-2000,'efectivo','Cambio histórico');
                    """;
                await seed.ExecuteNonQueryAsync();
            }

            var backups = Path.Combine(dir, "backups");
            Assert.Equal(new[] { 14, 15, 16 }, await migrador.MigrarAsync(MigracionesF01.Todas, backups));
            Assert.Single(Directory.GetFiles(backups, "*.db"));
            await using var upgraded = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await upgraded.OpenAsync();
            using var check = upgraded.CreateCommand();
            check.CommandText = "SELECT (SELECT COUNT(*) FROM cliente WHERE id='cliente-historico' AND limite_credito_minor IS NULL),(SELECT COUNT(*) FROM venta WHERE id='venta-historica' AND cliente_id='cliente-historico' AND cliente_nombre_snapshot='Cliente histórico'),(SELECT COUNT(*) FROM venta WHERE id='venta-sin-cliente' AND cliente_id IS NULL),(SELECT COUNT(*) FROM caja_movimiento WHERE id='caja-mov-historico' AND importe_minor=12000),(SELECT COUNT(*) FROM cambio WHERE id='cambio-historico' AND diferencia_minor=-2000),(SELECT COUNT(*) FROM cuenta_corriente_movimiento WHERE venta_id IN ('venta-historica','venta-sin-cliente'));";
            await using var result = await check.ExecuteReaderAsync(); Assert.True(await result.ReadAsync());
            Assert.Equal(1, result.GetInt32(0)); Assert.Equal(1, result.GetInt32(1)); Assert.Equal(1, result.GetInt32(2)); Assert.Equal(1, result.GetInt32(3)); Assert.Equal(1, result.GetInt32(4)); Assert.Equal(0, result.GetInt32(5));
            using var fk = upgraded.CreateCommand(); fk.CommandText = "PRAGMA foreign_key_check;"; await using var fkResult = await fk.ExecuteReaderAsync(); Assert.False(await fkResult.ReadAsync());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task V13_V14_son_aditivas_y_ventas_anteriores_quedan_sin_cliente_ni_snapshot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "clientes-v12.db");
        try
        {
            await new MigradorBaseDatos(path).MigrarAsync(MigracionesF01.Todas.Take(12).ToArray());
            await using (var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await db.OpenAsync();
                using var insert = db.CreateCommand();
                insert.CommandText = "INSERT INTO venta (id,numero,operacion_id,estado,total_minor,moneda,creada_en,confirmada_en) VALUES ('legacy-sale','T-000001','legacy-op','confirmada',1000,'ARS','2026-10-04T10:00:00Z','2026-10-04T10:00:00Z');";
                await insert.ExecuteNonQueryAsync();
            }

            var backups = Path.Combine(dir, "backups");
            Assert.Equal(new[] { 13, 14, 15, 16 }, await new MigradorBaseDatos(path).MigrarAsync(MigracionesF01.Todas, backups));
            Assert.Single(Directory.GetFiles(backups, "*.db"));
            await using var upgraded = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await upgraded.OpenAsync();
            using var check = upgraded.CreateCommand();
            check.CommandText = "SELECT numero, cliente_id, cliente_nombre_snapshot FROM venta WHERE id='legacy-sale';";
            await using var reader = await check.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("T-000001", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.True(reader.IsDBNull(2));
            await reader.DisposeAsync();
            using var fk = upgraded.CreateCommand();
            fk.CommandText = "PRAGMA foreign_keys=ON; UPDATE venta SET cliente_id='no-existe' WHERE id='legacy-sale';";
            await Assert.ThrowsAsync<SqliteException>(() => fk.ExecuteNonQueryAsync());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Alta_busqueda_edicion_y_baja_logica_conservan_contacto_y_permiten_mismo_telefono()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var id = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("  Ana Pérez  ", "  11-5555  ", "  Llamar por la tarde "), e.Sesion);
        var otro = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Ana P. ", "11-5555", null), e.Sesion);
        await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Ana Z.", null, null), e.Sesion);
        await Assert.ThrowsAsync<ArgumentException>(() => e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("   ", null, null), e.Sesion));

        var coincidencias = await e.Servicios.ConsultasClientes.BuscarAsync("11-", false, 10, 0, e.Sesion);
        Assert.Equal(2, coincidencias.Count);
        Assert.Contains(coincidencias, c => c.NombreCompleto == "Ana Pérez" && c.Telefono == "11-5555" && c.Observacion == "Llamar por la tarde");
        var pagina = await e.Servicios.ConsultasClientes.BuscarAsync("Ana", false, 2, 0, e.Sesion);
        Assert.Equal(3, pagina.Count); // página de 2 + una fila para indicar siguiente
        var paginaDos = await e.Servicios.ConsultasClientes.BuscarAsync("Ana", false, 2, 2, e.Sesion);
        Assert.Single(paginaDos);

        await e.Servicios.CasoEditarCliente.EjecutarAsync(id, new EdicionCliente("Ana Pérez García", "11-5555", "Preferencia actualizada"), e.Sesion);
        await e.Servicios.CasoDesactivarCliente.EjecutarAsync(id, e.Sesion);
        Assert.DoesNotContain(await e.Servicios.ConsultasClientes.BuscarSugerenciasAsync("Ana", 10, e.Sesion), x => x.Id == id);
        Assert.Contains(await e.Servicios.ConsultasClientes.BuscarAsync("Ana", true, 10, 0, e.Sesion), x => x.Id == id && !x.Activo);
        await e.Servicios.CasoDesactivarCliente.ReactivarAsync(id, e.Sesion);
        Assert.Contains(await e.Servicios.ConsultasClientes.BuscarSugerenciasAsync("Ana", 10, e.Sesion), x => x.Id == id);
        Assert.NotEqual(id, otro);
    }

    [Fact]
    public async Task Matriz_y_overrides_aplican_consulta_administracion_denegacion_y_concesion()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente visible", null, null), e.Sesion);
        var permisosDuena = await e.Servicios.Autorizador.ObtenerPermisosEfectivosAsync(e.Sesion, PermisosClientes.Todos);
        Assert.Contains(PermisosClientes.Consultar, permisosDuena);
        Assert.Contains(PermisosClientes.Administrar, permisosDuena);
        var empleadaId = await e.Servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("empleada-clientes", RolesLocal.Empleada), "clave-empleada-0000", e.Sesion);
        var sesionEmpleada = await e.Servicios.CasoIniciarSesion.EjecutarAsync("empleada-clientes", "clave-empleada-0000");

        var efectivos = await e.Servicios.Autorizador.ObtenerPermisosEfectivosAsync(sesionEmpleada.IdSesion, PermisosClientes.Todos);
        Assert.Contains(PermisosClientes.Consultar, efectivos);
        Assert.DoesNotContain(PermisosClientes.Administrar, efectivos);
        Assert.Single(await e.Servicios.ConsultasClientes.BuscarAsync("Cliente", false, 10, 0, sesionEmpleada.IdSesion));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("No permitido", null, null), sesionEmpleada.IdSesion));
        await e.Servicios.CasoConfigurarPermiso.EjecutarAsync(empleadaId, PermisosClientes.Consultar, false, e.Sesion);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicios.ConsultasClientes.BuscarAsync("Cliente", false, 10, 0, sesionEmpleada.IdSesion));
        await e.Servicios.CasoConfigurarPermiso.EjecutarAsync(empleadaId, PermisosClientes.Administrar, true, e.Sesion);
        await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Administrada por empleada autorizada", null, null), sesionEmpleada.IdSesion);
    }

    [Fact]
    public async Task Venta_opcional_guarda_snapshot_nombre_en_historial_y_ticket_aunque_cliente_cambie()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var clienteId = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("María Cliente", "11-1234", null), e.Sesion);
        var (_, variante, _) = await e.CatalogoConStockAsync();
        var precio = await e.PrecioDeAsync(variante);
        var venta = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)], [new NuevoPago(MediosPago.Efectivo, precio, precio)], "venta-con-cliente", e.Sesion, clienteId));

        await e.Servicios.CasoEditarCliente.EjecutarAsync(clienteId, new EdicionCliente("María Nuevo Nombre", "11-1234", null), e.Sesion);
        var ficha = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId, e.Sesion))!;
        Assert.Equal(clienteId, ficha.Venta.ClienteId);
        Assert.Equal("María Cliente", ficha.Venta.ClienteNombreSnapshot);
        var linea = Assert.Single(ficha.Lineas);
        var cambio = Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(
            venta.VentaId, [new LineaCambioEntrada(linea.Id, 1, true, null)], [new LineaCambioSalida(variante, 1, precio)], [], null, null, "cambio-cliente", e.Sesion)));
        var historial = await e.Servicios.ConsultasClientes.HistorialAsync(clienteId, 10, 0, e.Sesion);
        Assert.Equal(2, historial.Count);
        Assert.Contains(historial, movimiento => movimiento.Tipo == "venta" && movimiento.Referencia == venta.Numero && movimiento.TotalMinor == precio);
        Assert.Contains(historial, movimiento => movimiento.Tipo == "cambio" && movimiento.Referencia == cambio.Numero && movimiento.VentaReferencia == venta.Numero);
        var ticket = await e.Servicios.CasoGenerarTicket.EjecutarAsync(venta.VentaId, e.Sesion);
        Assert.Equal("María Cliente", ticket!.ClienteNombre);
        await e.Servicios.CasoDesactivarCliente.EjecutarAsync(clienteId, e.Sesion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)], [new NuevoPago(MediosPago.Efectivo, precio, precio)], "venta-cliente-inactivo", e.Sesion, clienteId));
        Assert.Equal("María Cliente", (await e.Servicios.CasoGenerarTicket.EjecutarAsync(venta.VentaId, e.Sesion))!.ClienteNombre);

        var clienteSinCuenta = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Cliente sin venta", null, null), e.Sesion);
        var ventaSinCliente = Assert.IsType<ResultadoConfirmacionVenta.Confirmada>(await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            [new NuevaLineaVenta(variante, 1, precio)], [new NuevoPago(MediosPago.Efectivo, precio, precio)], "venta-sin-cliente", e.Sesion));
        var fichaSinCliente = (await e.Servicios.ConsultasVentas.ObtenerFichaAsync(ventaSinCliente.VentaId, e.Sesion))!;
        Assert.Null(fichaSinCliente.Venta.ClienteId);
        Assert.Null(fichaSinCliente.Venta.ClienteNombreSnapshot);
        Assert.Empty(await e.Servicios.ConsultasClientes.HistorialAsync(clienteSinCuenta, 10, 0, e.Sesion));
    }

    [Fact]
    public async Task Vm_clientes_y_pos_exponen_seleccion_opcional_y_alta_condicionada_por_permiso()
    {
        using var e = await VentasTests.EntornoVentas.CrearAsync();
        var clienteId = await e.Servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente("Laura VM", "11-9876", null), e.Sesion);
        var vm = new ClientesViewModel(e.Servicios, () => e.Sesion, true);
        await vm.CargarAsync(true);
        Assert.Contains(vm.Clientes, c => c.Id == clienteId);
        vm.NuevoCommand.Execute(null);
        Assert.True(vm.FormularioVisible);
        vm.Seleccionado = vm.Clientes.Single(c => c.Id == clienteId);
        Assert.False(vm.FormularioVisible);
        Assert.Equal("Laura VM", vm.Nombre);

        var pos = new PosViewModel(e.Servicios, () => e.Sesion, puedeAsociarCliente: true);
        Assert.Null(pos.ClienteSeleccionado);
        Assert.True(pos.SinClienteSeleccionado);
        var sugerencia = Assert.Single(await e.Servicios.ConsultasClientes.BuscarSugerenciasAsync("Laura", 10, e.Sesion));
        pos.SeleccionarClienteCommand.Execute(sugerencia);
        Assert.Equal("Laura VM", pos.ClienteSeleccionado!.NombreCompleto);
        pos.LimpiarClienteCommand.Execute(null);
        Assert.True(pos.SinClienteSeleccionado);

        var sinPermiso = new ClientesViewModel(e.Servicios, () => e.Sesion, false);
        Assert.False(sinPermiso.PuedeAdministrar);
        var posSinPermiso = new PosViewModel(e.Servicios, () => e.Sesion, puedeAsociarCliente: false);
        Assert.False(posSinPermiso.PuedeAsociarCliente);
    }
}
