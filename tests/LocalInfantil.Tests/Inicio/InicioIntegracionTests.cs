using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Inicio;

/// <summary>
/// Integración real de las métricas de Inicio sobre una base temporal: la
/// última recepción sale de la base y las ventas del día sólo se leen con
/// permiso de reportes. Sin datos reales ni base provisional.
/// </summary>
public sealed class InicioIntegracionTests
{
    [Fact]
    public async Task Inicio_expone_la_recepcion_mas_reciente_y_respeta_el_permiso_de_ventas()
    {
        using var e = await EntornoInicio.CrearAsync();

        var productoId = await e.Svc.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera", null, "cat-rem"), e.SesionDuena);
        var varianteId = await e.Svc.CasoCrearVariante.EjecutarAsync(new NuevaVariante(productoId, null, null), e.SesionDuena);
        var proveedorId = await e.Svc.CasoCrearProveedor.EjecutarAsync(new NuevoProveedor("Ropa Sur"), e.SesionDuena);
        await e.Svc.CasoConfirmarRecepcion.EjecutarAsync(proveedorId,
            new[] { new LineaRecepcion(varianteId, 7, null, null) }, Guid.NewGuid().ToString("N"), null, e.SesionDuena);

        var comoDuenia = await e.Svc.ConsultasInicio.ObtenerAsync(e.SesionDuena);
        Assert.NotNull(comoDuenia.RecepcionReciente);
        Assert.Equal("Ropa Sur", comoDuenia.RecepcionReciente!.ProveedorNombre);
        Assert.Equal(7, comoDuenia.RecepcionReciente.Unidades);
        Assert.Equal(7, comoDuenia.Inventario.Disponible);
        Assert.NotNull(comoDuenia.VentasDelDia);
        Assert.Equal(0, comoDuenia.VentasDelDia!.CantidadVentas);

        var comoEmpleada = await e.Svc.ConsultasInicio.ObtenerAsync(e.SesionEmpleada);
        Assert.NotNull(comoEmpleada.RecepcionReciente);
        Assert.Null(comoEmpleada.VentasDelDia);
    }

    private sealed class EntornoInicio : IDisposable
    {
        public ServiciosComerciales Svc { get; }
        public string SesionDuena { get; }
        public string SesionEmpleada { get; }
        private readonly string _dir;

        private EntornoInicio(string dir, ServiciosComerciales svc, string sesionDuena, string sesionEmpleada)
        {
            _dir = dir;
            Svc = svc;
            SesionDuena = sesionDuena;
            SesionEmpleada = sesionEmpleada;
        }

        public static async Task<EntornoInicio> CrearAsync()
        {
            var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
            var ruta = Path.Combine(dir, "inicio.db");
            await new MigradorBaseDatos(ruta).MigrarAsync();
            var svc = new ServiciosComerciales(ruta);
            var duenaId = await svc.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
            var sesion = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
            foreach (var permiso in new[] { PermisosCatalogo.ProductosGestionar, PermisosCatalogo.VariantesGestionar, PermisosCompras.ProveedoresGestionar, PermisosCompras.RecepcionConfirmar })
                await svc.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);

            await svc.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("ana", RolesLocal.Empleada), "clave-ana-00000000", sesion.IdSesion);
            var empleada = await svc.CasoIniciarSesion.EjecutarAsync("ana", "clave-ana-00000000");
            return new EntornoInicio(dir, svc, sesion.IdSesion, empleada.IdSesion);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }
    }
}
