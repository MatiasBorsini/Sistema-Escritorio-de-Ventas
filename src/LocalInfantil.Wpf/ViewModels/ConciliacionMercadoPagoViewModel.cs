using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>Fila de liquidación pendiente para la pantalla de conciliación.</summary>
public sealed record FilaPendienteVm(
    string LiquidacionId,
    string Descripcion,
    string ClasificacionTexto,
    string EstadoTexto,
    long? ImporteMinor,
    string FechaTexto,
    string? Observacion);

/// <summary>Candidato de venta sugerido para conciliación asistida.</summary>
public sealed record CandidatoVm(
    string Origen,
    string? VentaId,
    string VentaTexto,
    long ImporteMinor,
    string FechaTexto,
    string Detalle);

/// <summary>Importación reciente registrada.</summary>
public sealed record ImportacionRecienteVm(
    string Archivo,
    string EstadoTexto,
    string Resumen,
    string FechaTexto);

/// <summary>
/// Conciliación de liquidaciones de Mercado Pago (solo Dueña): selecciona un
/// reporte, lo previsualiza (dry-run), lo importa de forma idempotente y
/// concilia pendientes de forma asistida. No inventa vínculos ni aplica nada
/// sin confirmación.
/// </summary>
public sealed class ConciliacionMercadoPagoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private byte[]? _contenido;
    private string? _nombreArchivo;
    private bool _ocupado;
    private string? _mensaje;
    private string? _error;
    private PrevisualizacionImportacionMp? _vistaPrevia;
    private FilaPendienteVm? _pendienteSeleccionada;
    private CandidatoVm? _candidatoSeleccionado;

    public ConciliacionMercadoPagoViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        SeleccionarArchivoCommand = new RelayCommand(_ => SeleccionarArchivo());
        PrevisualizarCommand = new AsyncRelayCommand(PrevisualizarAsync);
        ImportarCommand = new AsyncRelayCommand(ImportarAsync);
        ActualizarCommand = new AsyncRelayCommand(CargarAsync);
        BuscarCoincidenciasCommand = new AsyncRelayCommand(BuscarCoincidenciasAsync);
        ConciliarCommand = new AsyncRelayCommand(ConciliarAsync);
    }

    public bool Ocupado
    {
        get => _ocupado;
        private set
        {
            if (SetProperty(ref _ocupado, value))
            {
                OnPropertyChanged(nameof(PuedePrevisualizar));
                OnPropertyChanged(nameof(PuedeImportar));
                OnPropertyChanged(nameof(PuedeConciliar));
            }
        }
    }

    public string? Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }

    public string? NombreArchivo
    {
        get => _nombreArchivo;
        private set
        {
            if (SetProperty(ref _nombreArchivo, value))
                OnPropertyChanged(nameof(PuedePrevisualizar));
        }
    }

    public PrevisualizacionImportacionMp? VistaPrevia
    {
        get => _vistaPrevia;
        private set
        {
            if (SetProperty(ref _vistaPrevia, value))
            {
                OnPropertyChanged(nameof(HayPrevisualizacion));
                OnPropertyChanged(nameof(ResumenPrevisualizacion));
                OnPropertyChanged(nameof(YaImportado));
                OnPropertyChanged(nameof(PuedeImportar));
            }
        }
    }

    public bool HayPrevisualizacion => VistaPrevia is not null;
    public bool YaImportado => VistaPrevia?.YaImportado == true;
    public bool PuedePrevisualizar => !Ocupado && NombreArchivo is not null;
    public bool PuedeImportar => !Ocupado && VistaPrevia is not null && VistaPrevia.FilasValidas > 0 && !VistaPrevia.YaImportado;

    public string ResumenPrevisualizacion => VistaPrevia is null
        ? "Sin archivo seleccionado."
        : $"Archivo: {VistaPrevia.NombreArchivo} · Válidas: {VistaPrevia.FilasValidas} · Reconocidas: {VistaPrevia.FilasReconocidas} · " +
          $"Desconocidas: {VistaPrevia.FilasDesconocidas} · Duplicadas: {VistaPrevia.FilasDuplicadas} · Errores: {VistaPrevia.FilasConError}";

    public ObservableCollection<string> ErroresPreview { get; } = [];
    public ObservableCollection<FilaPendienteVm> Pendientes { get; } = [];
    public ObservableCollection<CandidatoVm> Candidatos { get; } = [];
    public ObservableCollection<ImportacionRecienteVm> Importaciones { get; } = [];

    public FilaPendienteVm? PendienteSeleccionada
    {
        get => _pendienteSeleccionada;
        set
        {
            if (SetProperty(ref _pendienteSeleccionada, value))
            {
                Candidatos.Clear();
                CandidatoSeleccionado = null;
                OnPropertyChanged(nameof(PuedeConciliar));
            }
        }
    }

    public CandidatoVm? CandidatoSeleccionado
    {
        get => _candidatoSeleccionado;
        set { if (SetProperty(ref _candidatoSeleccionado, value)) OnPropertyChanged(nameof(PuedeConciliar)); }
    }

    public bool PuedeConciliar => !Ocupado && PendienteSeleccionada is not null && CandidatoSeleccionado?.VentaId is not null;

    public RelayCommand SeleccionarArchivoCommand { get; }
    public AsyncRelayCommand PrevisualizarCommand { get; }
    public AsyncRelayCommand ImportarCommand { get; }
    public AsyncRelayCommand ActualizarCommand { get; }
    public AsyncRelayCommand BuscarCoincidenciasCommand { get; }
    public AsyncRelayCommand ConciliarCommand { get; }

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        Ocupado = true;
        try
        {
            var pendientes = await _servicios.ConsultaConciliacionMp.ListarPendientesAsync(_idSesion());
            Pendientes.Clear();
            foreach (var l in pendientes) Pendientes.Add(AMapear(l));

            var importaciones = await _servicios.ConsultaConciliacionMp.ListarImportacionesAsync(10, _idSesion());
            Importaciones.Clear();
            foreach (var i in importaciones)
                Importaciones.Add(new ImportacionRecienteVm(
                    i.ArchivoNombre ?? "—",
                    EstadoConciliacionTexto(i.Estado),
                    $"{i.FilasAplicadas} aplicadas · {i.FilasError} con error",
                    FechaLocalTexto(i.CreadoEn)));
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private void SeleccionarArchivo()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleccionar reporte de liquidación de Mercado Pago",
            Filter = "Reportes de Mercado Pago (*.csv;*.txt)|*.csv;*.txt|Todos los archivos (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialogo.ShowDialog() != true) return;
        try
        {
            _contenido = File.ReadAllBytes(dialogo.FileName);
            NombreArchivo = Path.GetFileName(dialogo.FileName);
            VistaPrevia = null;
            ErroresPreview.Clear();
            Mensaje = null;
            Error = null;
        }
        catch (Exception ex)
        {
            _contenido = null;
            Error = $"No se pudo leer el archivo: {ex.Message}";
        }
    }

    private async Task PrevisualizarAsync()
    {
        if (_contenido is null || NombreArchivo is null || Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            var previa = await _servicios.CasoPrevisualizarImportacionMp.EjecutarAsync(_contenido, NombreArchivo, _idSesion());
            VistaPrevia = previa;
            ErroresPreview.Clear();
            foreach (var e in previa.Errores)
                ErroresPreview.Add($"Fila {e.NumeroFila}: {e.Motivo}");
            if (previa.YaImportado)
                Mensaje = "Este archivo ya fue importado. No hace falta aplicarlo de nuevo.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ImportarAsync()
    {
        if (_contenido is null || NombreArchivo is null || Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        var completado = false;
        try
        {
            var resultado = await _servicios.CasoImportarLiquidacionMp.EjecutarAsync(_contenido, NombreArchivo, null, _idSesion());
            Mensaje = $"Importación {EstadoConciliacionTexto(resultado.Estado)}: {resultado.FilasAplicadas} aplicadas, " +
                      $"{resultado.FilasDuplicadas} duplicadas, {resultado.FilasError} con error, {resultado.FilasEnRevision} en revisión. " +
                      $"Conciliadas automáticamente: {resultado.ConciliadasAuto}. Pendientes: {resultado.Pendientes}.";
            VistaPrevia = null;
            ErroresPreview.Clear();
            completado = true;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
        if (completado) await CargarAsync();
    }

    private async Task BuscarCoincidenciasAsync()
    {
        if (PendienteSeleccionada is null || Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            var candidatos = await _servicios.CasoConciliarLiquidacionMp.SugerirAsync(PendienteSeleccionada.LiquidacionId, _idSesion());
            Candidatos.Clear();
            foreach (var c in candidatos)
                Candidatos.Add(new CandidatoVm(
                    c.Origen,
                    c.VentaId,
                    c.VentaNumero ?? c.VentaId ?? "—",
                    c.ImporteMinor,
                    FechaLocalTexto(c.Fecha),
                    c.Detalle));
            CandidatoSeleccionado = null;
            if (Candidatos.Count == 0)
                Mensaje = "Sin coincidencias automáticas. Revisá la venta manualmente antes de vincular.";
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task ConciliarAsync()
    {
        if (PendienteSeleccionada is null || CandidatoSeleccionado?.VentaId is not { } ventaId || Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        var completado = false;
        try
        {
            var resultado = await _servicios.CasoConciliarLiquidacionMp.ConfirmarAsync(PendienteSeleccionada.LiquidacionId, ventaId, _idSesion());
            Mensaje = resultado.EstadoConciliacion == EstadosConciliacionCobroManualMp.Conciliada
                ? "Movimiento conciliado con la venta elegida."
                : $"Conciliación registrada con estado {EstadoConciliacionTexto(resultado.EstadoConciliacion)}. {resultado.DiferenciaDetalle}";
            completado = true;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
        if (completado)
        {
            Candidatos.Clear();
            CandidatoSeleccionado = null;
            await CargarAsync();
        }
    }

    private static FilaPendienteVm AMapear(LiquidacionMpRegistrada l) => new(
        l.Id,
        l.Descripcion,
        ClasificacionTexto(l.Clasificacion),
        EstadoConciliacionTexto(l.EstadoConciliacion),
        l.GrossMinor ?? l.NetCreditoMinor,
        FechaLocalTexto(l.Fecha ?? l.FechaTransaccion),
        l.Observacion);

    private static string ClasificacionTexto(string clasificacion) => clasificacion switch
    {
        ClasificacionMovimientoMp.Cobro => "Cobro",
        ClasificacionMovimientoMp.Devolucion => "Devolución",
        ClasificacionMovimientoMp.Contracargo => "Contracargo",
        ClasificacionMovimientoMp.Impuesto => "Impuesto",
        ClasificacionMovimientoMp.PagoFueraSistema => "Pago fuera del sistema",
        ClasificacionMovimientoMp.Transferencia => "Transferencia",
        ClasificacionMovimientoMp.Saldo => "Saldo",
        ClasificacionMovimientoMp.Otro => "Otro",
        _ => "No reconocido"
    };

    private static string EstadoConciliacionTexto(string estado) => estado switch
    {
        EstadosConciliacionCobroManualMp.Conciliada => "Conciliado",
        EstadosConciliacionCobroManualMp.ConDiferencia => "Con diferencia",
        EstadosConciliacionCobroManualMp.NoRelacionada => "No relacionada",
        EstadosConciliacionCobroManualMp.RequiereIntervencion => "Requiere revisión",
        EstadosConciliacionCobroManualMp.Pendiente => "Pendiente",
        EstadosImportacionMp.Validado => "Validado",
        EstadosImportacionMp.Aplicado => "Aplicado",
        EstadosImportacionMp.Parcial => "Parcial",
        EstadosImportacionMp.Rechazado => "Rechazado",
        _ => estado
    };

    private static string FechaLocalTexto(string? iso)
        => string.IsNullOrEmpty(iso) ? "—"
            : DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var f)
                ? f.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                : iso;
}
