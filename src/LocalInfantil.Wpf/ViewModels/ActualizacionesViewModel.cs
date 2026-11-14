using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Actualizaciones;
using LocalInfantil.Infrastructure.Actualizaciones;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Módulo Actualizaciones: comprobación y descarga automática verificadas;
/// instalar requiere permiso y confirmación explícita de la Dueña.
/// </summary>
public sealed class ActualizacionesViewModel : ViewModelBase
{
    private readonly Func<string?> _idSesion;
    private readonly ICoordinadorActualizaciones? _coordinador;
    private readonly bool _esProduccion;
    private readonly bool _puedeInstalarComoDuena;
    private CancellationTokenSource? _descargaCts;
    private EstadoActualizacion _estado = EstadoActualizacion.Actualizado;
    private string? _mensaje;
    private string? _error;
    private bool _ocupado;
    private long _bytesDescargados;
    private long _bytesTotales;

    public ActualizacionesViewModel(
        ServiciosComerciales servicios,
        Func<string?> idSesion,
        bool esProduccion,
        bool puedeInstalarComoDuena,
        ICoordinadorActualizaciones? coordinador = null)
    {
        _idSesion = idSesion;
        _esProduccion = esProduccion;
        _puedeInstalarComoDuena = puedeInstalarComoDuena;
        _coordinador = !esProduccion ? null : coordinador ?? CrearCoordinadorProduccion(servicios);
        if (!esProduccion) _estado = EstadoActualizacion.DeshabilitadasEnDesarrollo;
        else if (_coordinador?.UltimoManifiesto is not null) _estado = EstadoActualizacion.ListaParaInstalar;
        VersionInstalada = VersionAplicacion.Obtener();
        ComprobarCommand = new AsyncRelayCommand(ComprobarAsync);
        CancelarDescargaCommand = new RelayCommand(_ => _descargaCts?.Cancel());
        InstalarCommand = new AsyncRelayCommand(InstalarAsync);
    }

    private static ICoordinadorActualizaciones CrearCoordinadorProduccion(ServiciosComerciales servicios)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var almacen = new AlmacenActualizaciones(
            RutaBaseDatosProductiva.ObtenerDirActualizaciones(),
            Path.Combine(RutaBaseDatosProductiva.ObtenerDirRespaldos(), "Locks"));
        almacen.RecuperarAlIniciar();
        var puerta = new PuertaInstalacion(servicios.RepoCaja, servicios.Fabrica, almacen);
        return new CoordinadorActualizaciones(
            new CanalHttpActualizaciones(http),
            new DescargadorHttpActualizaciones(http),
            new VerificadorFirmaActualizacion(),
            almacen,
            puerta,
            servicios.Autorizador,
            VersionAplicacion.Obtener(),
            servicios.Reloj);
    }

    public string VersionInstalada { get; }
    public string? VersionDisponible => _coordinador?.UltimoManifiesto?.Version;
    public string? NotasVersion => _coordinador?.UltimoManifiesto?.NotasVersion;
    public string? UltimaComprobacionUtc => _coordinador?.UltimaComprobacionUtc;
    public EstadoActualizacion Estado
    {
        get => _estado;
        private set { if (SetProperty(ref _estado, value)) { NotificarDerivados(); OnPropertyChanged(nameof(EstadoTexto)); } }
    }
    public string EstadoTexto => Estado switch
    {
        EstadoActualizacion.Actualizado => "Actualizado",
        EstadoActualizacion.CanalNoConfigurado => "Canal de actualizaciones no configurado",
        EstadoActualizacion.DeshabilitadasEnDesarrollo => "Deshabilitadas en modo desarrollo",
        EstadoActualizacion.Buscando => "Buscando",
        EstadoActualizacion.ActualizacionDisponible => "Actualización disponible",
        EstadoActualizacion.Descargando => "Descargando",
        EstadoActualizacion.ListaParaInstalar => "Actualización lista",
        EstadoActualizacion.InstalarDespuesDeCerrarCaja => "Esperando cierre de caja",
        EstadoActualizacion.SinConexion => "Sin conexión",
        EstadoActualizacion.ErrorActualizacion => "Error",
        _ => "Estado desconocido",
    };
    public string? Mensaje
    {
        get => _mensaje;
        private set => SetProperty(ref _mensaje, value);
    }
    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }
    public bool Ocupado
    {
        get => _ocupado;
        private set { if (SetProperty(ref _ocupado, value)) NotificarDerivados(); }
    }
    public long BytesDescargados
    {
        get => _bytesDescargados;
        private set { if (SetProperty(ref _bytesDescargados, value)) OnPropertyChanged(nameof(ProgresoTexto)); }
    }
    public long BytesTotales
    {
        get => _bytesTotales;
        private set { if (SetProperty(ref _bytesTotales, value)) OnPropertyChanged(nameof(ProgresoTexto)); }
    }
    public string ProgresoTexto => BytesTotales > 0 ? $"{BytesDescargados / 1024} KB de {BytesTotales / 1024} KB" : string.Empty;

    public bool PuedeComprobar => !Ocupado;
    public bool Descargando => Ocupado && Estado == EstadoActualizacion.Descargando;
    public bool PuedeInstalar => _esProduccion && _puedeInstalarComoDuena && !Ocupado && Estado == EstadoActualizacion.ListaParaInstalar;

    public AsyncRelayCommand ComprobarCommand { get; }
    public RelayCommand CancelarDescargaCommand { get; }
    public AsyncRelayCommand InstalarCommand { get; }

    public async Task CargarAsync()
    {
        if (_esProduccion) await ComprobarAsync();
    }

    private async Task ComprobarAsync()
    {
        if (Ocupado) return;
        if (!_esProduccion || _coordinador is null)
        {
            Estado = EstadoActualizacion.DeshabilitadasEnDesarrollo;
            Mensaje = "Actualizaciones deshabilitadas en modo desarrollo.";
            return;
        }
        Ocupado = true; Mensaje = null; Error = null;
        Estado = EstadoActualizacion.Buscando;
        BytesTotales = 0;
        BytesDescargados = 0;
        using (_descargaCts = new CancellationTokenSource())
        try
        {
            var progreso = new Progress<long>(b =>
            {
                Estado = EstadoActualizacion.Descargando;
                BytesDescargados = b;
                BytesTotales = _coordinador.UltimoManifiesto?.TamanoBytes ?? 0;
            });
            Estado = await _coordinador.ComprobarAsync(_idSesion(), progreso, _descargaCts.Token);
            Error = _coordinador.UltimoError;
            Mensaje = Estado switch
            {
                EstadoActualizacion.Actualizado => $"Estás al día (versión {VersionInstalada}).",
                EstadoActualizacion.CanalNoConfigurado => "Actualizaciones no configuradas en este equipo.",
                EstadoActualizacion.DeshabilitadasEnDesarrollo => "Actualizaciones deshabilitadas en modo desarrollo.",
                EstadoActualizacion.SinConexion => "Sin conexión al canal. La versión actual sigue funcionando.",
                EstadoActualizacion.ListaParaInstalar => $"Versión {VersionDisponible} lista para instalar.",
                EstadoActualizacion.Descargando => "Descargando actualización en segundo plano…",
                _ => Mensaje,
            };
        }
        catch (OperationCanceledException)
        {
            Estado = _coordinador.UltimoManifiesto is not null ? EstadoActualizacion.ActualizacionDisponible : EstadoActualizacion.SinConexion;
            Mensaje = "Comprobación/descarga cancelada. La versión actual sigue funcionando.";
        }
        finally { Ocupado = false; _descargaCts?.Dispose(); _descargaCts = null; }
    }

    private async Task InstalarAsync()
    {
        if (!_esProduccion || !_puedeInstalarComoDuena || _coordinador is null) return;
        if (Ocupado) return;
        Ocupado = true; Mensaje = null; Error = null;
        try
        {
            var puerta = await _coordinador.AdquirirInstalacionSeguraAsync(_idSesion());
            if (!puerta.Permitido)
            {
                Estado = EstadoActualizacion.InstalarDespuesDeCerrarCaja;
                Mensaje = puerta.Motivo;
                return;
            }

            var lease = puerta.Lease!;
            var leaseTransferido = false;
            var instalador = puerta.RutaInstalador!;
            try
            {
                var confirm = MessageBox.Show(
                    $"Se va a instalar la versión {VersionDisponible} y cerrar la aplicación.\n\n" +
                    "Asegurate de haber cerrado la caja.\n¿Continuar?",
                    "Instalar actualización",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes)
                {
                    Mensaje = "Instalación cancelada. La versión actual sigue funcionando.";
                    return;
                }

                var proceso = Process.Start(new ProcessStartInfo(instalador) { UseShellExecute = true });
                if (proceso is null) throw new InvalidOperationException("Windows no inició el instalador.");
                if (System.Windows.Application.Current is App aplicacion)
                {
                    aplicacion.MantenerLeaseInstalacionHastaSalir(lease);
                    leaseTransferido = true;
                }
                System.Windows.Application.Current.Shutdown();
            }
            finally { if (!leaseTransferido) await lease.DisposeAsync(); }
        }
        catch (Exception ex)
        {
            Error = $"No se pudo iniciar el instalador. La aplicación continúa sin cambios: {ex.Message}";
        }
        finally { Ocupado = false; }
    }

    private void NotificarDerivados()
    {
        OnPropertyChanged(nameof(VersionDisponible));
        OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(NotasVersion));
        OnPropertyChanged(nameof(UltimaComprobacionUtc));
        OnPropertyChanged(nameof(PuedeComprobar));
        OnPropertyChanged(nameof(Descargando));
        OnPropertyChanged(nameof(PuedeInstalar));
        OnPropertyChanged(nameof(Ocupado));
    }
}
