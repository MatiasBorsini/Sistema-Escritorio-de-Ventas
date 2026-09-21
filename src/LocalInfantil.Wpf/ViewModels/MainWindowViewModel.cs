using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Impresion;
using LocalInfantil.Application.Features.Persistencia;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private ViewModelBase _paginaActual;

    public MainWindowViewModel(
        ComprobarPersistenciaLocal comprobarPersistenciaLocal,
        GenerarDocumentoImpresionDePrueba generarDocumentoImpresionDePrueba,
        Action<DocumentoImpresionDePrueba> abrirVistaPreviaImpresion)
    {
        ArgumentNullException.ThrowIfNull(comprobarPersistenciaLocal);
        ArgumentNullException.ThrowIfNull(generarDocumentoImpresionDePrueba);
        ArgumentNullException.ThrowIfNull(abrirVistaPreviaImpresion);

        Inicio = new InicioViewModel();
        EstadoPrototipo = new EstadoPrototipoViewModel(
            comprobarPersistenciaLocal,
            generarDocumentoImpresionDePrueba,
            abrirVistaPreviaImpresion);
        _paginaActual = Inicio;
        NavigateCommand = new RelayCommand(Navigate);
    }

    public InicioViewModel Inicio { get; }

    public EstadoPrototipoViewModel EstadoPrototipo { get; }

    public ICommand NavigateCommand { get; }

    public ViewModelBase PaginaActual
    {
        get => _paginaActual;
        private set
        {
            if (SetProperty(ref _paginaActual, value))
            {
                OnPropertyChanged(nameof(TituloPaginaActual));
            }
        }
    }

    public string TituloPaginaActual => PaginaActual switch
    {
        InicioViewModel => "Inicio",
        EstadoPrototipoViewModel => "Estado del prototipo",
        _ => string.Empty
    };

    private void Navigate(object? page)
    {
        if (page is ViewModelBase viewModel)
        {
            PaginaActual = viewModel;
        }
    }
}
