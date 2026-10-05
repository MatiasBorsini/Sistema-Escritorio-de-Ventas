using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class ClientesViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private ClienteRegistrado? _seleccionado;
    private string _busqueda = string.Empty;
    private string _nombre = string.Empty;
    private string _telefono = string.Empty;
    private string _observacion = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _incluirInactivos;
    private bool _modoEdicion;
    private bool _formularioVisible;
    private bool _haySiguiente;
    private bool _ocupado;
    private bool _confirmacionDesactivacionVisible;
    private int _desplazamiento;
    private ResumenCuentaCorriente? _resumenCuenta;
    private const int TamanoPagina = 50;

    public ClientesViewModel(ServiciosComerciales servicios, Func<string?> idSesion, bool puedeAdministrar, bool puedeConsultarCuenta = false)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        PuedeAdministrar = puedeAdministrar;
        PuedeConsultarCuenta = puedeConsultarCuenta;
        Clientes = new(); Historial = new(); HistorialCuenta = new();
        BuscarCommand = new AsyncRelayCommand(() => CargarAsync(true));
        AnteriorCommand = new AsyncRelayCommand(() => CargarAsync(false, -TamanoPagina));
        SiguienteCommand = new AsyncRelayCommand(() => CargarAsync(false, TamanoPagina));
        NuevoCommand = new RelayCommand(_ => AbrirAlta());
        EditarCommand = new RelayCommand(_ => AbrirEdicion());
        CancelarEdicionCommand = new RelayCommand(_ => CerrarFormulario());
        GuardarCommand = new AsyncRelayCommand(GuardarAsync);
        CambiarActivoCommand = new AsyncRelayCommand(SolicitarCambioActivoAsync);
        ConfirmarDesactivacionCommand = new AsyncRelayCommand(CambiarActivoAsync);
        CancelarDesactivacionCommand = new RelayCommand(_ => ConfirmacionDesactivacionVisible = false);
    }

    public ObservableCollection<ClienteRegistrado> Clientes { get; }
    public ObservableCollection<FilaHistorialCliente> Historial { get; }
    public ObservableCollection<FilaHistorialCliente> HistorialCuenta { get; }
    public bool PuedeAdministrar { get; }
    public bool PuedeConsultarCuenta { get; }
    public ResumenCuentaCorriente? ResumenCuenta { get => _resumenCuenta; private set => SetProperty(ref _resumenCuenta, value); }
    public bool Ocupado { get => _ocupado; private set => SetProperty(ref _ocupado, value); }
    public bool ConfirmacionDesactivacionVisible { get => _confirmacionDesactivacionVisible; private set => SetProperty(ref _confirmacionDesactivacionVisible, value); }
    public string Busqueda { get => _busqueda; set => SetProperty(ref _busqueda, value); }
    public bool IncluirInactivos { get => _incluirInactivos; set => SetProperty(ref _incluirInactivos, value); }
    public ClienteRegistrado? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (!SetProperty(ref _seleccionado, value)) return;
            OnPropertyChanged(nameof(HaySeleccion));
            OnPropertyChanged(nameof(EstadoSeleccion));
            ConfirmacionDesactivacionVisible = false;
            if (value is not null) { _modoEdicion = false; CargarDatos(value); FormularioVisible = false; OnPropertyChanged(nameof(TituloFormulario)); _ = CargarHistorialAsync(value.Id); _ = CargarCuentaAsync(value.Id); }
            else { Historial.Clear(); HistorialCuenta.Clear(); ResumenCuenta = null; CerrarFormulario(); }
        }
    }
    public bool HaySeleccion => Seleccionado is not null;
    public string EstadoSeleccion => Seleccionado is null ? "Seleccioná un cliente para ver sus datos e historial." : (Seleccionado.Activo ? "Activo" : "Inactivo");
    public string Nombre { get => _nombre; set => SetProperty(ref _nombre, value); }
    public string Telefono { get => _telefono; set => SetProperty(ref _telefono, value); }
    public string Observacion { get => _observacion; set => SetProperty(ref _observacion, value); }
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; private set => SetProperty(ref _mensaje, value); }
    public bool FormularioVisible { get => _formularioVisible; private set => SetProperty(ref _formularioVisible, value); }
    public string TituloFormulario => _modoEdicion ? "Editar cliente" : "Nuevo cliente";
    public string TextoAccionEstado => Seleccionado?.Activo == true ? "Desactivar" : "Reactivar";
    public bool HaySiguiente { get => _haySiguiente; private set => SetProperty(ref _haySiguiente, value); }
    public bool HayAnterior => _desplazamiento > 0;
    public string TextoPagina => $"Página {_desplazamiento / TamanoPagina + 1}";
    public AsyncRelayCommand BuscarCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public RelayCommand NuevoCommand { get; }
    public RelayCommand EditarCommand { get; }
    public RelayCommand CancelarEdicionCommand { get; }
    public AsyncRelayCommand GuardarCommand { get; }
    public AsyncRelayCommand CambiarActivoCommand { get; }
    public AsyncRelayCommand ConfirmarDesactivacionCommand { get; }
    public RelayCommand CancelarDesactivacionCommand { get; }

    public async Task CargarAsync(bool reiniciar = false, int delta = 0)
    {
        if (reiniciar) _desplazamiento = 0;
        else _desplazamiento = Math.Max(0, _desplazamiento + delta);
        Error = null; Ocupado = true;
        try
        {
            var rows = await _servicios.ConsultasClientes.BuscarAsync(Busqueda, IncluirInactivos, TamanoPagina, _desplazamiento, _idSesion());
            HaySiguiente = rows.Count > TamanoPagina;
            var selectedId = Seleccionado?.Id;
            Clientes.Clear(); foreach (var row in rows.Take(TamanoPagina)) Clientes.Add(row);
            Seleccionado = selectedId is null ? null : Clientes.FirstOrDefault(c => c.Id == selectedId);
            OnPropertyChanged(nameof(HayAnterior)); OnPropertyChanged(nameof(TextoPagina));
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private void AbrirAlta()
    {
        Seleccionado = null; _modoEdicion = false; Nombre = Telefono = Observacion = string.Empty;
        OnPropertyChanged(nameof(TituloFormulario)); FormularioVisible = true; Error = Mensaje = null;
    }
    private void AbrirEdicion()
    {
        if (!PuedeAdministrar || Seleccionado is null || !Seleccionado.Activo) return;
        _modoEdicion = true; CargarDatos(Seleccionado);
        OnPropertyChanged(nameof(TituloFormulario)); FormularioVisible = true; Error = Mensaje = null;
    }
    private void CargarDatos(ClienteRegistrado cliente)
    {
        Nombre = cliente.NombreCompleto; Telefono = cliente.Telefono ?? string.Empty; Observacion = cliente.Observacion ?? string.Empty;
        Mensaje = null; OnPropertyChanged(nameof(TextoAccionEstado));
    }
    private void CerrarFormulario() { FormularioVisible = false; Error = null; }

    private async Task GuardarAsync()
    {
        Error = null; Mensaje = null;
        try
        {
            Ocupado = true;
            if (_modoEdicion && Seleccionado is not null)
                await _servicios.CasoEditarCliente.EjecutarAsync(Seleccionado.Id, new EdicionCliente(Nombre, Telefono, Observacion), _idSesion());
            else
                await _servicios.CasoCrearCliente.EjecutarAsync(new NuevoCliente(Nombre, Telefono, Observacion), _idSesion());
            Mensaje = _modoEdicion ? "Los datos del cliente se actualizaron." : "El cliente se creó correctamente.";
            FormularioVisible = false; await CargarAsync(true);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task CambiarActivoAsync()
    {
        if (Seleccionado is null) return;
        Error = null; Mensaje = null;
        try
        {
            Ocupado = true;
            if (Seleccionado.Activo) await _servicios.CasoDesactivarCliente.EjecutarAsync(Seleccionado.Id, _idSesion());
            else await _servicios.CasoDesactivarCliente.ReactivarAsync(Seleccionado.Id, _idSesion());
            Mensaje = Seleccionado.Activo ? "El cliente se desactivó." : "El cliente se reactivó.";
            ConfirmacionDesactivacionVisible = false;
            await CargarAsync(true);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private Task SolicitarCambioActivoAsync()
    {
        if (Seleccionado is null) return Task.CompletedTask;
        if (Seleccionado.Activo)
        {
            ConfirmacionDesactivacionVisible = true;
            return Task.CompletedTask;
        }
        return CambiarActivoAsync();
    }

    private async Task CargarHistorialAsync(string clienteId)
    {
        Historial.Clear(); HistorialCuenta.Clear();
        try
        {
            var rows = await _servicios.ConsultasClientes.HistorialAsync(clienteId, 30, 0, _idSesion());
            if (Seleccionado?.Id != clienteId) return;
            Historial.Clear(); HistorialCuenta.Clear();
            foreach (var row in rows.Take(30))
            {
                var fila = FilaHistorialCliente.Desde(row);
                if (row.Tipo is "cargo_venta" or "abono" or "ajuste_positivo" or "ajuste_negativo" or "credito_devolucion" or "cancelacion_deuda") HistorialCuenta.Add(fila);
                else Historial.Add(fila);
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    private async Task CargarCuentaAsync(string clienteId)
    {
        ResumenCuenta = null;
        if (!PuedeConsultarCuenta) return;
        try
        {
            var resumen = await _servicios.ConsultasCuentaCorriente.ObtenerAsync(clienteId, _idSesion());
            if (Seleccionado?.Id == clienteId) ResumenCuenta = resumen;
        }
        catch (Exception ex) { Error = ex.Message; }
    }
}

public sealed record FilaHistorialCliente(string Fecha, string Descripcion)
{
    public static FilaHistorialCliente Desde(MovimientoHistorialCliente movimiento)
    {
        var fecha = DateTimeOffset.TryParse(movimiento.Fecha, out var instante) ? instante.ToLocalTime().ToString("g") : movimiento.Fecha;
        if (movimiento.Tipo == "venta")
        {
            var estado = movimiento.Estado == "anulada" ? "Anulada" : "Confirmada";
            return new(fecha, $"Venta {movimiento.Referencia} · {PosViewModel.FormatearArs(movimiento.TotalMinor ?? 0)} · {estado}");
        }
        if (movimiento.Tipo is "cargo_venta" or "abono" or "ajuste_positivo" or "ajuste_negativo" or "credito_devolucion" or "cancelacion_deuda")
        {
            var texto = movimiento.Tipo switch
            {
                "cargo_venta" => "Cargo de venta",
                "abono" => "Abono",
                "ajuste_positivo" => "Ajuste que aumenta deuda",
                "ajuste_negativo" => "Ajuste que reduce deuda",
                "credito_devolucion" => "Crédito de devolución",
                _ => "Cancelación de deuda",
            };
            var importe = PosViewModel.FormatearArs(movimiento.TotalMinor ?? 0);
            return new(fecha, $"Cuenta corriente · {texto} · {movimiento.Referencia} · {importe}");
        }
        var diferencia = movimiento.DiferenciaMinor switch
        {
            > 0 => $"Diferencia cobrada {PosViewModel.FormatearArs(movimiento.DiferenciaMinor.Value)}",
            < 0 => $"Diferencia reintegrada {PosViewModel.FormatearArs(Math.Abs(movimiento.DiferenciaMinor.Value))}",
            _ => "Sin diferencia",
        };
        return new(fecha, $"Cambio {movimiento.Referencia} · Venta {movimiento.VentaReferencia} · {diferencia}");
    }
}
