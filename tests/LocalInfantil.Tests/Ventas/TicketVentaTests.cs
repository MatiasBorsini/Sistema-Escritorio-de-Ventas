using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Ventas;

/// <summary>
/// F04-T04: el comprobante se reconstruye desde snapshots persistidos y la
/// reimpresión nunca muta registros.
/// </summary>
public sealed class TicketVentaTests
{
    [Fact]
    public async Task Ticket_reconstruye_desde_snapshots_y_reimprimir_no_muta()
    {
        using var e = await EntornoTicket.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera Ticket", "Nube", "cat-rem"), e.Sesion);
        var v = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Negro"), e.Sesion);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v, 15000, e.Sesion);
        await e.Servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(v, 5) }, Guid.NewGuid().ToString("N"), e.Sesion);
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v, 2, 15000) },
            new[] { new NuevoPago(MediosPago.Efectivo, 30000, 35000) },
            "op-ticket-001",
            e.Sesion);

        var ticket = (await e.Servicios.CasoGenerarTicket.EjecutarAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal(ok.Numero, ticket.Numero);
        Assert.Equal("confirmada", ticket.Estado);
        var linea = Assert.Single(ticket.Lineas);
        Assert.Equal("Remera Ticket", linea.NombreProducto);
        Assert.Equal("M", linea.Talle);
        Assert.Equal("Negro", linea.Color);
        Assert.Equal(2, linea.Cantidad);
        Assert.Equal(15000, linea.PrecioUnitarioMinor);
        Assert.Equal(30000, linea.SubtotalMinor);
        Assert.Equal(30000, ticket.TotalMinor);
        var pago = Assert.Single(ticket.Pagos);
        Assert.Equal(MediosPago.Efectivo, pago.Medio);
        Assert.Equal(35000, pago.RecibidoMinor);
        Assert.Equal(5000, pago.VueltoMinor);
        Assert.Null(ticket.MotivoAnulacion);

        // Cambios posteriores no alteran el comprobante histórico.
        await e.Servicios.CasoEditarProducto.EjecutarAsync(prod, new EdicionProducto("Otro nombre", null, "cat-buz"), e.Sesion);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v, 99999, e.Sesion);
        var releido = (await e.Servicios.CasoGenerarTicket.EjecutarAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal("Remera Ticket", releido.Lineas[0].NombreProducto);
        Assert.Equal(15000, releido.Lineas[0].PrecioUnitarioMinor);

        // Reimprimir no muta: mismo contenido y mismos conteos.
        var antes = await e.ContarAsync();
        var otraVez = (await e.Servicios.CasoGenerarTicket.EjecutarAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal(ticket.Numero, otraVez.Numero);
        Assert.Equal(ticket.TotalMinor, otraVez.TotalMinor);
        Assert.Equal(ticket.Lineas.Count, otraVez.Lineas.Count);
        Assert.Equal(ticket.Lineas[0], otraVez.Lineas[0]);
        Assert.Equal(ticket.Pagos.Count, otraVez.Pagos.Count);
        Assert.Equal(ticket.Pagos[0], otraVez.Pagos[0]);
        Assert.Equal(antes, await e.ContarAsync());
    }

    [Fact]
    public async Task Ticket_de_anulada_muestra_motivo()
    {
        using var e = await EntornoTicket.CrearAsync();
        var prod = await e.Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Buzo Ticket", null, "cat-buz"), e.Sesion);
        var v = await e.Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.Sesion);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v, 20000, e.Sesion);
        await e.Servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(v, 3) }, Guid.NewGuid().ToString("N"), e.Sesion);
        var ok = (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(
            new[] { new NuevaLineaVenta(v, 1, 20000) },
            new[] { new NuevoPago(MediosPago.Debito, 20000, null) },
            "op-ticket-002",
            e.Sesion);
        await e.Servicios.CasoAnularVenta.EjecutarAsync(ok.VentaId, "Talle equivocado", "op-anul-ticket", e.Sesion);

        var ticket = (await e.Servicios.CasoGenerarTicket.EjecutarAsync(ok.VentaId, e.Sesion))!;
        Assert.Equal("anulada", ticket.Estado);
        Assert.Equal("Talle equivocado", ticket.MotivoAnulacion);
        Assert.Equal(20000, ticket.TotalMinor);
    }

    private sealed class EntornoTicket : IDisposable
    {
        public ServiciosComerciales Servicios { get; }
        public string Sesion { get; }
        private readonly string _dir;
        private readonly string _ruta;

        private EntornoTicket(string dir, ServiciosComerciales servicios, string sesion)
        {
            _dir = dir;
            Servicios = servicios;
            Sesion = sesion;
            _ruta = Path.Combine(dir, "ticket-f04.db");
        }

        public static async Task<EntornoTicket> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "ticket-f04.db");
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

            return new EntornoTicket(dir, servicios, sesion.IdSesion);
        }

        public async Task<(long Ventas, long Movimientos, long Pagos)> ContarAsync()
        {
            await using var conexion = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = _ruta,
                Pooling = false,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
            }.ToString());
            await conexion.OpenAsync();
            async Task<long> Contar(string tabla)
            {
                using var comando = conexion.CreateCommand();
                comando.CommandText = $"SELECT COUNT(*) FROM {tabla};";
                return Convert.ToInt64(await comando.ExecuteScalarAsync());
            }

            return (await Contar("venta"), await Contar("movimiento_inventario"), await Contar("pago"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
