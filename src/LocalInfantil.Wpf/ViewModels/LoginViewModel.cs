using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<SesionUsuario, Task> _alAutenticar;
    private string _nombre = string.Empty;
    private string _contrasena = string.Empty;
    private string? _error;
    private bool _ocupado;

    public LoginViewModel(ServiciosComerciales servicios, Func<SesionUsuario, Task> alAutenticar)
    {
        _servicios = servicios;
        _alAutenticar = alAutenticar;
        IngresarCommand = new AsyncRelayCommand(IngresarAsync);
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

    public AsyncRelayCommand IngresarCommand { get; }

    private async Task IngresarAsync()
    {
        if (Ocupado) return;
        Error = null;
        if (string.IsNullOrWhiteSpace(Nombre) || string.IsNullOrWhiteSpace(Contrasena))
        {
            Error = "Ingresá nombre y contraseña.";
            return;
        }

        Ocupado = true;
        try
        {
            var sesion = await _servicios.CasoIniciarSesion.EjecutarAsync(Nombre.Trim(), Contrasena);
            Contrasena = string.Empty;
            await _alAutenticar(sesion);
        }
        catch (CredencialesInvalidasException)
        {
            Error = "Credenciales inválidas.";
        }
        catch (Exception)
        {
            Error = "No se pudo iniciar sesión. Intentá nuevamente.";
        }
        finally
        {
            Ocupado = false;
        }
    }
}
