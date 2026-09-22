using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class BootstrapDuenaViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<SesionUsuario, Task> _alCompletar;
    private string _nombre = string.Empty;
    private string _contrasena = string.Empty;
    private string _confirmacion = string.Empty;
    private string? _error;
    private bool _ocupado;

    public BootstrapDuenaViewModel(ServiciosComerciales servicios, Func<SesionUsuario, Task> alCompletar)
    {
        _servicios = servicios;
        _alCompletar = alCompletar;
        CrearCommand = new AsyncRelayCommand(CrearAsync);
    }

    public string Nombre
    {
        get => _nombre;
        set => SetProperty(ref _nombre, value);
    }

    public string Contrasena
    {
        get => _contrasena;
        set => SetProperty(ref _contrasena, value);
    }

    public string Confirmacion
    {
        get => _confirmacion;
        set => SetProperty(ref _confirmacion, value);
    }

    public string? Error
    {
        get => _error;
        set => SetProperty(ref _error, value);
    }

    public bool Ocupado
    {
        get => _ocupado;
        set => SetProperty(ref _ocupado, value);
    }

    public AsyncRelayCommand CrearCommand { get; }

    private async Task CrearAsync()
    {
        if (Ocupado) return;
        Error = null;
        if (string.IsNullOrWhiteSpace(Nombre))
        {
            Error = "Ingresá el nombre de la Dueña.";
            return;
        }

        if (Contrasena.Length < 12)
        {
            Error = "La contraseña debe tener al menos 12 caracteres.";
            return;
        }

        if (Contrasena != Confirmacion)
        {
            Error = "Las contraseñas no coinciden.";
            return;
        }

        Ocupado = true;
        try
        {
            var id = await _servicios.CasoCrearUsuario.EjecutarAsync(
                new NuevoUsuario(Nombre.Trim(), RolesLocal.Duena), Contrasena, null);
            // auto-login tras bootstrap
            var sesion = await _servicios.CasoIniciarSesion.EjecutarAsync(Nombre.Trim(), Contrasena);
            // limpiar secretos
            Contrasena = string.Empty;
            Confirmacion = string.Empty;
            await _alCompletar(sesion);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = ex.Message;
        }
        catch (Exception)
        {
            Error = "No se pudo crear la cuenta. Verificá los datos.";
        }
        finally
        {
            Ocupado = false;
        }
    }
}
