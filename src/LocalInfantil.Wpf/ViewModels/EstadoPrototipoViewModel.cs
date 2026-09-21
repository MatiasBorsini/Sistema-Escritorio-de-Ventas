using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Impresion;
using LocalInfantil.Application.Features.Persistencia;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class EstadoPrototipoViewModel : ViewModelBase
{
    private readonly ComprobarPersistenciaLocal _comprobarPersistenciaLocal;
    private readonly GenerarDocumentoImpresionDePrueba _generarDocumentoImpresionDePrueba;
    private readonly Action<DocumentoImpresionDePrueba> _abrirVistaPreviaImpresion;
    private string _estadoComprobacion = "Aún no se ejecutó la comprobación de SQLite.";
    private string _estadoEscritura = "Pendiente";
    private string _estadoLectura = "Pendiente";
    private string _estadoCoincidencia = "Pendiente";
    private string _mensajeError = string.Empty;
    private string _estadoImpresion = "Aún no se abrió la vista previa de impresión.";

    public EstadoPrototipoViewModel(
        ComprobarPersistenciaLocal comprobarPersistenciaLocal,
        GenerarDocumentoImpresionDePrueba generarDocumentoImpresionDePrueba,
        Action<DocumentoImpresionDePrueba> abrirVistaPreviaImpresion)
    {
        _comprobarPersistenciaLocal = comprobarPersistenciaLocal ?? throw new ArgumentNullException(nameof(comprobarPersistenciaLocal));
        _generarDocumentoImpresionDePrueba = generarDocumentoImpresionDePrueba ?? throw new ArgumentNullException(nameof(generarDocumentoImpresionDePrueba));
        _abrirVistaPreviaImpresion = abrirVistaPreviaImpresion ?? throw new ArgumentNullException(nameof(abrirVistaPreviaImpresion));
        ComprobarPersistenciaCommand = new AsyncRelayCommand(ComprobarPersistenciaAsync);
        AbrirVistaPreviaImpresionCommand = new RelayCommand(AbrirVistaPreviaImpresion);
    }

    public IReadOnlyList<EstadoItem> Elementos { get; } =
    [
        new("Interfaz", "Navegación de demostración disponible"),
        new("Datos", "Comprobación técnica de SQLite disponible bajo demanda"),
        new("Servicios externos", "No configurados")
    ];

    public ICommand ComprobarPersistenciaCommand { get; }

    public ICommand AbrirVistaPreviaImpresionCommand { get; }

    public string EstadoImpresion
    {
        get => _estadoImpresion;
        private set => SetProperty(ref _estadoImpresion, value);
    }

    public string EstadoComprobacion
    {
        get => _estadoComprobacion;
        private set => SetProperty(ref _estadoComprobacion, value);
    }

    public string EstadoEscritura
    {
        get => _estadoEscritura;
        private set => SetProperty(ref _estadoEscritura, value);
    }

    public string EstadoLectura
    {
        get => _estadoLectura;
        private set => SetProperty(ref _estadoLectura, value);
    }

    public string EstadoCoincidencia
    {
        get => _estadoCoincidencia;
        private set => SetProperty(ref _estadoCoincidencia, value);
    }

    public string MensajeError
    {
        get => _mensajeError;
        private set => SetProperty(ref _mensajeError, value);
    }

    private async Task ComprobarPersistenciaAsync()
    {
        EstadoComprobacion = "Comprobando persistencia local…";
        EstadoEscritura = "En curso";
        EstadoLectura = "En curso";
        EstadoCoincidencia = "En curso";
        MensajeError = string.Empty;

        var resultado = await _comprobarPersistenciaLocal.EjecutarAsync();

        EstadoEscritura = resultado.EscrituraCorrecta ? "Correcta" : "No completada";
        EstadoLectura = resultado.LecturaCorrecta ? "Correcta" : "No completada";
        EstadoCoincidencia = resultado.DatoCoincide ? "Correcta" : "No coincide";
        MensajeError = resultado.MensajeError ?? string.Empty;
        EstadoComprobacion = resultado.Correcta
            ? "Comprobación de SQLite completada."
            : "La comprobación de SQLite no pudo completarse.";
    }

    private void AbrirVistaPreviaImpresion(object? parameter)
    {
        try
        {
            var documento = _generarDocumentoImpresionDePrueba.Generar();
            _abrirVistaPreviaImpresion(documento);
            EstadoImpresion = "Vista previa de impresión cerrada.";
        }
        catch (Exception exception)
        {
            EstadoImpresion = $"No se pudo abrir la vista previa de impresión: {exception.Message}";
        }
    }
}

public sealed record EstadoItem(string Nombre, string Estado);
