using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Inicio;
using LocalInfantil.Application.Features.Reportes;

namespace LocalInfantil.Tests.Inicio;

public sealed class ConsultasInicioTests
{
    [Fact]
    public async Task Resume_inventario_pendientes_y_actividad_humana_con_lecturas_agregadas()
    {
        var repositorio = new RepositorioInicioFalso
        {
            Inventario = new(14, 10, 4),
            Conteos = 2,
            Recepcion = new("Ropa Sur", 7, "2026-09-24T11:00:00Z"),
            Eventos =
            [
                new("inventario.apartado", "REM-110", 4, null, "ana", "2026-09-24T10:30:00Z"),
                new("auditoria.interna", null, null, null, "ana", "2026-09-24T10:00:00Z")
            ]
        };
        var consultas = new ConsultasInicio(
            new AutorizadorFalso(new HashSet<string> { ConsultasInicio.RecibirMercaderia }),
            repositorio,
            new FabricaFalsa());

        var resultado = await consultas.ObtenerAsync("sesion-prueba");

        Assert.Equal(14, resultado.Inventario.Fisico);
        Assert.Equal(10, resultado.Inventario.Disponible);
        Assert.Equal(4, resultado.Inventario.Apartado);
        Assert.Equal(2, resultado.ConteosPendientes);
        Assert.Contains(ConsultasInicio.RecibirMercaderia, resultado.AccionesPermitidas);
        Assert.Single(resultado.Actividad);
        Assert.Equal("Mercadería apartada", resultado.Actividad[0].Descripcion);
        Assert.Equal("REM-110", resultado.Actividad[0].Referencia);
        Assert.Equal(6, repositorio.LimiteSolicitado);
        Assert.Equal(1, repositorio.LecturasInventario);
        Assert.Equal(1, repositorio.LecturasConteos);
        Assert.Equal(1, repositorio.LecturasActividad);
        Assert.Equal("Ropa Sur", resultado.RecepcionReciente!.ProveedorNombre);
        Assert.Equal(7, resultado.RecepcionReciente.Unidades);
        Assert.Null(resultado.VentasDelDia);
        Assert.Equal(0, repositorio.LecturasVentas);
        Assert.Equal(1, repositorio.LecturasRecepcion);
    }

    [Fact]
    public async Task Ventas_del_dia_se_incluyen_solo_con_permiso_de_reportes()
    {
        var repositorio = new RepositorioInicioFalso { Ventas = new(123450, 3) };
        var consultasConPermiso = new ConsultasInicio(
            new AutorizadorFalso(new HashSet<string> { PermisosReportes.Ventas }),
            repositorio,
            new FabricaFalsa());

        var conPermiso = await consultasConPermiso.ObtenerAsync("sesion-prueba");
        Assert.Equal(123450, conPermiso.VentasDelDia!.NetoMinor);
        Assert.Equal(3, conPermiso.VentasDelDia.CantidadVentas);
        Assert.Equal(1, repositorio.LecturasVentas);

        var sinPermiso = await new ConsultasInicio(
            new AutorizadorFalso(new HashSet<string>()),
            new RepositorioInicioFalso { Ventas = new(123450, 3) },
            new FabricaFalsa()).ObtenerAsync("sesion-prueba");
        Assert.Null(sinPermiso.VentasDelDia);
    }

    [Fact]
    public async Task Sin_recepciones_el_resumen_las_expone_como_ausentes()
    {
        var consultas = new ConsultasInicio(
            new AutorizadorFalso(new HashSet<string>()),
            new RepositorioInicioFalso(),
            new FabricaFalsa());

        var resultado = await consultas.ObtenerAsync("sesion-prueba");

        Assert.Null(resultado.RecepcionReciente);
    }

    private sealed class AutorizadorFalso(IReadOnlySet<string> permisos) : IAutorizador
    {
        public Task<UsuarioRegistrado> AutorizarAsync(string idSesion, string permiso, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> ObtenerPermisosEfectivosAsync(string idSesion, IReadOnlyCollection<string> solicitados, CancellationToken cancellationToken = default)
            => Task.FromResult(permisos);
    }

    private sealed class FabricaFalsa : IFabricaSesionesPersistencia
    {
        public Task<ISesionPersistencia> IniciarAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ISesionPersistencia> IniciarLecturaAsync(CancellationToken cancellationToken = default) => Task.FromResult<ISesionPersistencia>(new SesionFalsa());
    }

    private sealed class SesionFalsa : ISesionPersistencia
    {
        public Task ConfirmarAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task RevertirAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RepositorioInicioFalso : IRepositorioInicio
    {
        public ResumenInventarioInicio Inventario { get; init; } = new(0, 0, 0);
        public int Conteos { get; init; }
        public VentasDelDiaInicio Ventas { get; init; } = new(0, 0);
        public RecepcionRecienteInicio? Recepcion { get; init; }
        public IReadOnlyList<EventoInicioOrigen> Eventos { get; init; } = [];
        public int LimiteSolicitado { get; private set; }
        public int LecturasInventario { get; private set; }
        public int LecturasConteos { get; private set; }
        public int LecturasActividad { get; private set; }
        public int LecturasVentas { get; private set; }
        public int LecturasRecepcion { get; private set; }
        public Task<ResumenInventarioInicio> ObtenerInventarioAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default) { LecturasInventario++; return Task.FromResult(Inventario); }
        public Task<int> ContarConteosBorradorAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default) { LecturasConteos++; return Task.FromResult(Conteos); }
        public Task<IReadOnlyList<EventoInicioOrigen>> ListarActividadAsync(ISesionPersistencia sesion, int limite, CancellationToken cancellationToken = default) { LecturasActividad++; LimiteSolicitado = limite; return Task.FromResult(Eventos); }
        public Task<VentasDelDiaInicio> ObtenerVentasDelDiaAsync(ISesionPersistencia sesion, string desdeUtc, string hastaUtc, CancellationToken cancellationToken = default) { LecturasVentas++; return Task.FromResult(Ventas); }
        public Task<RecepcionRecienteInicio?> ObtenerUltimaRecepcionAsync(ISesionPersistencia sesion, CancellationToken cancellationToken = default) { LecturasRecepcion++; return Task.FromResult(Recepcion); }
    }
}
