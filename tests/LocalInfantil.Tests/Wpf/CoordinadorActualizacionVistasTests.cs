using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Wpf;

public sealed class CoordinadorActualizacionVistasTests
{
    [Fact]
    public async Task Vista_activa_se_actualiza_cuando_termina_la_operacion_que_protegia_el_borrador()
    {
        var notificador = new NotificadorCambiosDatos();
        using var coordinador = new CoordinadorActualizacionVistas(notificador);
        var vista = new VistaTemporal();
        var recargada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cantidadRecargas = 0;

        coordinador.Registrar(vista, [AreaDatos.Proveedores], () =>
        {
            Interlocked.Increment(ref cantidadRecargas);
            recargada.TrySetResult();
            return Task.CompletedTask;
        }, () => vista.PuedeActualizar);
        coordinador.Activar(vista);

        notificador.Publicar(new object(), AreaDatos.Proveedores, AreaDatos.Proveedores);
        Assert.Equal(0, cantidadRecargas);

        vista.PuedeActualizar = true;
        await recargada.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, cantidadRecargas);
    }

    [Fact]
    public async Task Registro_reemplazado_y_coordinador_dispuesto_no_dejan_handlers_duplicados()
    {
        var notificador = new NotificadorCambiosDatos();
        var coordinador = new CoordinadorActualizacionVistas(notificador);
        var vista = new VistaTemporal { PuedeActualizar = true };
        var recargadas = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cantidadRecargas = 0;
        Func<Task> cargar = () =>
        {
            if (Interlocked.Increment(ref cantidadRecargas) == 1) recargadas.TrySetResult();
            return Task.CompletedTask;
        };

        coordinador.Registrar(vista, [AreaDatos.Proveedores], cargar);
        coordinador.Registrar(vista, [AreaDatos.Proveedores], cargar);
        coordinador.Activar(vista);
        notificador.Publicar(new object(), AreaDatos.Proveedores, AreaDatos.Proveedores);
        await recargadas.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, cantidadRecargas);

        coordinador.Dispose();
        notificador.Publicar(new object(), AreaDatos.Proveedores, AreaDatos.Proveedores);
        Assert.Equal(1, cantidadRecargas);
    }

    private sealed class VistaTemporal : ViewModelBase
    {
        private bool _puedeActualizar;
        public bool PuedeActualizar
        {
            get => _puedeActualizar;
            set => SetProperty(ref _puedeActualizar, value);
        }
    }
}
