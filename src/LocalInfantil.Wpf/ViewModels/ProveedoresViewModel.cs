using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class ProveedoresViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _nuevoNombre = string.Empty;
    private string _edicionNombre = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private bool _soloActivos = true;
    private ProveedorRegistrado? _seleccionado;
    private bool _confirmacionVisible;
    private string? _nombrePendiente;

    public ProveedoresViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Proveedores = new ObservableCollection<ProveedorRegistrado>();
        CrearCommand = new AsyncRelayCommand(CrearAsync);
        EditarCommand = new AsyncRelayCommand(EditarAsync);
        SolicitarDesactivarCommand = new AsyncRelayCommand(SolicitarDesactivarAsync);
        ConfirmarDesactivarCommand = new AsyncRelayCommand(ConfirmarDesactivarAsync);
        CancelarDesactivarCommand = new AsyncRelayCommand(CancelarDesactivarAsync);
        RefrescarCommand = new AsyncRelayCommand(CargarAsync);
    }

    public ObservableCollection<ProveedorRegistrado> Proveedores { get; }
    public ProveedorRegistrado? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (SetProperty(ref _seleccionado, value) && value is not null)
                EdicionNombre = value.Nombre;
        }
    }
    public string NuevoNombre { get => _nuevoNombre; set => SetProperty(ref _nuevoNombre, value); }
    public string EdicionNombre { get => _edicionNombre; set => SetProperty(ref _edicionNombre, value); }
    public bool SoloActivos { get => _soloActivos; set { if (SetProperty(ref _soloActivos, value)) _ = CargarAsync(); } }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public bool ConfirmacionVisible { get => _confirmacionVisible; set => SetProperty(ref _confirmacionVisible, value); }
    public string? NombrePendiente { get => _nombrePendiente; set => SetProperty(ref _nombrePendiente, value); }

    public AsyncRelayCommand CrearCommand { get; }
    public AsyncRelayCommand EditarCommand { get; }
    public AsyncRelayCommand SolicitarDesactivarCommand { get; }
    public AsyncRelayCommand ConfirmarDesactivarCommand { get; }
    public AsyncRelayCommand CancelarDesactivarCommand { get; }
    public AsyncRelayCommand RefrescarCommand { get; }

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var lista = await _servicios.ConsultasProveedores.ListarAsync(_idSesion(), SoloActivos);
            Proveedores.Clear();
            foreach (var p in lista) Proveedores.Add(p);
            if (Seleccionado is not null) Seleccionado = Proveedores.FirstOrDefault(x => x.Id == Seleccionado.Id);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task CrearAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(NuevoNombre)) { Error = "Nombre obligatorio."; return; }
        Ocupado = true;
        try
        {
            var id = await _servicios.CasoCrearProveedor.EjecutarAsync(new NuevoProveedor(NuevoNombre.Trim()), _idSesion());
            Mensaje = $"Proveedor creado {id}.";
            NuevoNombre = string.Empty;
            OnPropertyChanged(nameof(NuevoNombre));
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private async Task EditarAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un proveedor."; return; }
        if (string.IsNullOrWhiteSpace(EdicionNombre)) { Error = "Nombre obligatorio."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoEditarProveedor.EjecutarAsync(Seleccionado.Id, new EdicionProveedor(EdicionNombre.Trim()), _idSesion());
            Mensaje = "Proveedor editado.";
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public Task SolicitarDesactivarAsyncPublic() => SolicitarDesactivarAsync();
    public Task CancelarDesactivarAsyncPublic() => CancelarDesactivarAsync();
    public Task ConfirmarDesactivarAsyncPublic() => ConfirmarDesactivarAsync();
    private Task SolicitarDesactivarAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un proveedor."; return Task.CompletedTask; }
        if (!Seleccionado.Activo) { Error = "Ya está inactivo."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        NombrePendiente = Seleccionado.Nombre;
        ConfirmacionVisible = true;
        return Task.CompletedTask;
    }
    private Task CancelarDesactivarAsync() { ConfirmacionVisible = false; NombrePendiente = null; return Task.CompletedTask; }
    private async Task ConfirmarDesactivarAsync()
    {
        if (Seleccionado is null || !ConfirmacionVisible) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarProveedor.EjecutarAsync(Seleccionado.Id, _idSesion());
            Mensaje = "Proveedor desactivado (con historial conservado).";
            ConfirmacionVisible = false; NombrePendiente = null;
            await CargarAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida." : ex.Message;
}
