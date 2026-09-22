using System.Collections.ObjectModel;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class PermisoFilaViewModel : ViewModelBase
{
    private bool? _estadoExplicito;
    private bool _efectivo;
    private bool _ocupado;

    public string Permiso { get; }
    public bool? EstadoExplicito
    {
        get => _estadoExplicito;
        set => SetProperty(ref _estadoExplicito, value);
    }

    public bool Efectivo
    {
        get => _efectivo;
        set => SetProperty(ref _efectivo, value);
    }

    public bool Ocupado
    {
        get => _ocupado;
        set => SetProperty(ref _ocupado, value);
    }

    public string TextoEstado => EstadoExplicito is null
        ? (Efectivo ? "heredado concedido" : "denegado por defecto")
        : (EstadoExplicito.Value ? "concedido" : "denegado");

    public void NotificarTextoEstado() => OnPropertyChanged(nameof(TextoEstado));

    public PermisoFilaViewModel(string permiso) => Permiso = permiso;
}

public sealed class UsuarioFilaViewModel : ViewModelBase
{
    public UsuarioRegistrado Usuario { get; }
    public ObservableCollection<PermisoFilaViewModel> Permisos { get; } = new();

    public UsuarioFilaViewModel(UsuarioRegistrado usuario, IEnumerable<string> permisosCatalogo)
    {
        Usuario = usuario;
        foreach (var p in permisosCatalogo)
        {
            Permisos.Add(new PermisoFilaViewModel(p));
        }
    }

    public string Nombre => Usuario.Nombre;
    public string Rol => Usuario.Rol;
    public bool Activo => Usuario.Activo;
    public string Id => Usuario.Id;
}

public sealed class AdministracionUsuariosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string> _idSesion;
    private string _nuevoNombre = string.Empty;
    private string _nuevaContrasena = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private UsuarioFilaViewModel? _seleccionado;
    private bool _confirmacionDesactivacionVisible;
    private string? _nombrePendienteDesactivacion;

    public AdministracionUsuariosViewModel(ServiciosComerciales servicios, Func<string> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Usuarios = new ObservableCollection<UsuarioFilaViewModel>();
        CrearCommand = new AsyncRelayCommand(CrearAsync);
        SolicitarDesactivacionCommand = new AsyncRelayCommand(SolicitarDesactivacionAsync);
        ConfirmarDesactivacionCommand = new AsyncRelayCommand(ConfirmarDesactivacionAsync);
        CancelarDesactivacionCommand = new AsyncRelayCommand(CancelarDesactivacionAsync);
        RestablecerCommand = new AsyncRelayCommand(RestablecerAsync);
        RefrescarCommand = new AsyncRelayCommand(() => CargarAsync());
#pragma warning disable CS0618
        DesactivarCommand = SolicitarDesactivacionCommand;
#pragma warning restore CS0618
    }

    public ObservableCollection<UsuarioFilaViewModel> Usuarios { get; }
    public UsuarioFilaViewModel? Seleccionado
    {
        get => _seleccionado;
        set
        {
            if (SetProperty(ref _seleccionado, value))
            {
                _ = CargarPermisosSeleccionadoAsync();
            }
        }
    }

    public string NuevoNombre
    {
        get => _nuevoNombre;
        set => SetProperty(ref _nuevoNombre, value);
    }

    public string NuevaContrasena
    {
        get => _nuevaContrasena;
        set => SetProperty(ref _nuevaContrasena, value);
    }

    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public string? Mensaje
    {
        get => _mensaje;
        set => SetProperty(ref _mensaje, value);
    }

    public bool Ocupado
    {
        get => _ocupado;
        set => SetProperty(ref _ocupado, value);
    }

    public AsyncRelayCommand CrearCommand { get; }
    [Obsolete("Usar SolicitarDesactivacionCommand con confirmación explícita")]
    public AsyncRelayCommand DesactivarCommand { get; }
    public AsyncRelayCommand SolicitarDesactivacionCommand { get; }
    public AsyncRelayCommand ConfirmarDesactivacionCommand { get; }
    public AsyncRelayCommand CancelarDesactivacionCommand { get; }
    public AsyncRelayCommand RestablecerCommand { get; }
    public AsyncRelayCommand RefrescarCommand { get; }

    public bool ConfirmacionDesactivacionVisible
    {
        get => _confirmacionDesactivacionVisible;
        set => SetProperty(ref _confirmacionDesactivacionVisible, value);
    }

    public string? NombrePendienteDesactivacion
    {
        get => _nombrePendienteDesactivacion;
        set => SetProperty(ref _nombrePendienteDesactivacion, value);
    }

    public async Task CargarAsync()
    {
        Error = null;
        Mensaje = null;
        try
        {
            await using var s = await _servicios.Fabrica.IniciarLecturaAsync();
            var lista = await _servicios.RepoUsuarios.ListarTodosAsync(s);
            Usuarios.Clear();
            foreach (var u in lista)
            {
                Usuarios.Add(new UsuarioFilaViewModel(u, ServiciosComerciales.PermisosComercialesConfigurables));
            }

            if (Seleccionado is not null)
            {
                Seleccionado = Usuarios.FirstOrDefault(x => x.Id == Seleccionado.Id);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    public async Task AlternarPermisoAsync(PermisoFilaViewModel fila, bool conceder)
    {
        if (Seleccionado is null) return;
        Error = null;
        Mensaje = null;
        fila.Ocupado = true;
        try
        {
            await _servicios.CasoConfigurarPermiso.EjecutarAsync(Seleccionado.Id, fila.Permiso, conceder, _idSesion());
            await CargarPermisosSeleccionadoAsync();
            Mensaje = conceder ? "Permiso concedido." : "Permiso denegado.";
        }
        catch (SesionInvalidaException)
        {
            Error = "Sesión vencida. Volvé a iniciar sesión.";
        }
        catch (AccesoDenegadoException)
        {
            Error = "No tenés permiso para administrar usuarios.";
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            fila.Ocupado = false;
        }
    }

    private async Task CrearAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        if (string.IsNullOrWhiteSpace(NuevoNombre) || string.IsNullOrWhiteSpace(NuevaContrasena))
        {
            Error = "Ingresá nombre y contraseña inicial.";
            return;
        }

        if (NuevaContrasena.Length < 12)
        {
            Error = "La contraseña debe tener al menos 12 caracteres.";
            return;
        }

        Ocupado = true;
        try
        {
            await _servicios.CasoCrearUsuario.EjecutarAsync(
                new NuevoUsuario(NuevoNombre.Trim(), RolesLocal.Empleada), NuevaContrasena, _idSesion());
            NuevoNombre = string.Empty;
            NuevaContrasena = string.Empty;
            Mensaje = "Empleado creado.";
            await CargarAsync();
        }
        catch (SesionInvalidaException) { Error = "Sesión vencida."; }
        catch (AccesoDenegadoException) { Error = "No autorizado."; }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    public Task SolicitarDesactivacionAsyncPublic() => SolicitarDesactivacionAsync();
    public Task CancelarDesactivacionAsyncPublic() => CancelarDesactivacionAsync();
    public Task ConfirmarDesactivacionAsyncPublic() => ConfirmarDesactivacionAsync();

    private Task SolicitarDesactivacionAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un usuario."; return Task.CompletedTask; }
        if (Seleccionado.Rol == RolesLocal.Duena) { Error = "No se desactiva Dueña desde esta lista."; return Task.CompletedTask; }
        if (!Seleccionado.Activo) { Error = "El usuario ya está inactivo."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        NombrePendienteDesactivacion = Seleccionado.Nombre;
        ConfirmacionDesactivacionVisible = true;
        return Task.CompletedTask;
    }

    private Task CancelarDesactivacionAsync()
    {
        ConfirmacionDesactivacionVisible = false;
        NombrePendienteDesactivacion = null;
        return Task.CompletedTask;
    }

    private async Task ConfirmarDesactivacionAsync()
    {
        if (Seleccionado is null || !ConfirmacionDesactivacionVisible) return;
        Error = null; Mensaje = null;
        Ocupado = true;
        try
        {
            await _servicios.CasoDesactivarUsuario.EjecutarAsync(Seleccionado.Id, _idSesion());
            Mensaje = "Empleado desactivado.";
            ConfirmacionDesactivacionVisible = false;
            NombrePendienteDesactivacion = null;
            await CargarAsync();
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task RestablecerAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un usuario."; return; }
        if (string.IsNullOrWhiteSpace(NuevaContrasena) || NuevaContrasena.Length < 12)
        {
            Error = "Ingresá la nueva contraseña (mín. 12).";
            return;
        }

        Error = null; Mensaje = null;
        Ocupado = true;
        try
        {
            await _servicios.CasoRestablecerContrasena.EjecutarAsync(Seleccionado.Id, NuevaContrasena, _idSesion(), null);
            Mensaje = "Contraseña restablecida.";
            NuevaContrasena = string.Empty;
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { Ocupado = false; }
    }

    private async Task CargarPermisosSeleccionadoAsync()
    {
        if (Seleccionado is null) return;
        try
        {
            await using var s = await _servicios.Fabrica.IniciarLecturaAsync();
            foreach (var fila in Seleccionado.Permisos)
            {
                var explicito = await _servicios.RepoPermisos.ObtenerAsync(s, Seleccionado.Id, fila.Permiso);
                fila.EstadoExplicito = explicito;
                // efectivo: explicito prevalece, sino matriz
                fila.Efectivo = explicito ?? _servicios.Matriz.Permite(Seleccionado.Rol, fila.Permiso);
                fila.NotificarTextoEstado();
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
