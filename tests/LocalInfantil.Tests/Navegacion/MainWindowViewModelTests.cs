using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Impresion;
using LocalInfantil.Application.Features.Persistencia;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Tests.Navegacion;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void Navega_al_estado_del_prototipo_sin_servicios_externos()
    {
        var viewModel = new MainWindowViewModel(
            new ComprobarPersistenciaLocal(new PersistenciaNoUsada()),
            new GenerarDocumentoImpresionDePrueba(),
            _ => { });

        viewModel.NavigateCommand.Execute(viewModel.EstadoPrototipo);

        Assert.Same(viewModel.EstadoPrototipo, viewModel.PaginaActual);
        Assert.Equal("Estado del prototipo", viewModel.TituloPaginaActual);
    }

    private sealed class PersistenciaNoUsada : IPersistenciaTecnicaDePrueba
    {
        public Task InicializarAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task GuardarAsync(string clave, string valor, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
