using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class AppRootViewModel : ViewModelBase
{
    private readonly ServiciosComerciales? _servicios;
    private readonly string? _rutaDev;
    private readonly string? _errorInicial;
    private ViewModelBase? _contenido;
    private SesionUsuario? _sesion;

    public AppRootViewModel(ServiciosComerciales? servicios, string? rutaDev, string? errorInicial, ViewModelBase contenidoInicial)
    {
        _servicios = servicios;
        _rutaDev = rutaDev;
        _errorInicial = errorInicial;
        _contenido = contenidoInicial;
    }

    public ViewModelBase? Contenido
    {
        get => _contenido;
        set => SetProperty(ref _contenido, value);
    }

    public SesionUsuario? Sesion
    {
        get => _sesion;
        set
        {
            if (SetProperty(ref _sesion, value))
            {
                OnPropertyChanged(nameof(EstaAutenticado));
                OnPropertyChanged(nameof(Identidad));
            }
        }
    }

    public bool EstaAutenticado => Sesion is not null;
    public string Identidad => Sesion is null ? string.Empty : $"{Sesion.Nombre} ({Sesion.Rol})";
    public string? ErrorInicial => _errorInicial;
    public string? RutaDev => _rutaDev;
    public bool ModoDesarrollo => _servicios is not null;

    public void Mostrar(ViewModelBase vm) => Contenido = vm;

    public async Task CerrarSesionAsync()
    {
        if (_servicios is not null && Sesion is not null)
        {
            try { await _servicios.CasoCerrarSesion.EjecutarAsync(Sesion.IdSesion); } catch { }
        }

        Sesion = null;
        if (_servicios is not null)
        {
            // volver a login/bootstrap según estado
            await VerificarEstadoAsync();
        }
    }

    public async Task VerificarEstadoAsync()
    {
        if (_servicios is null) return;
        await using var sesion = await _servicios.Fabrica.IniciarLecturaAsync();
        var total = await _servicios.RepoUsuarios.ContarTodosAsync(sesion);
        if (total == 0)
        {
            Contenido = new BootstrapDuenaViewModel(_servicios, async s => await AutenticadoAsync(s));
        }
        else
        {
            Contenido = new LoginViewModel(_servicios, async s => await AutenticadoAsync(s));
        }
    }

    public async Task AutenticadoAsync(SesionUsuario s)
    {
        Sesion = s;
        await MostrarShellAsync();
    }

    private async Task MostrarShellAsync()
    {
        if (_servicios is null || Sesion is null) return;
        var catalogo = new CatalogoViewModel(_servicios, () => Sesion.IdSesion);
        if (Sesion.Rol == "duena")
        {
            var admin = new AdministracionUsuariosViewModel(_servicios, () => Sesion.IdSesion);
            await admin.CargarAsync();
            Contenido = new ShellDuenaViewModel(Sesion, admin, catalogo, CerrarSesionAsync);
        }
        else
        {
            Contenido = new ShellEmpleadaViewModel(Sesion, catalogo, CerrarSesionAsync);
        }
    }
}

public sealed class ShellDuenaViewModel : ViewModelBase
{
    public SesionUsuario Sesion { get; }
    public AdministracionUsuariosViewModel Administracion { get; }
    public CatalogoViewModel Catalogo { get; }
    public Func<Task> CerrarSesion { get; }
    public string Identidad => $"{Sesion.Nombre} ({Sesion.Rol}) — Dueña";

    public ShellDuenaViewModel(SesionUsuario sesion, AdministracionUsuariosViewModel admin, CatalogoViewModel catalogo, Func<Task> cerrar)
    {
        Sesion = sesion;
        Administracion = admin;
        Catalogo = catalogo;
        CerrarSesion = cerrar;
    }
}

public sealed class ShellEmpleadaViewModel : ViewModelBase
{
    public SesionUsuario Sesion { get; }
    public CatalogoViewModel Catalogo { get; }
    public Func<Task> CerrarSesion { get; }
    public string Identidad => $"{Sesion.Nombre} ({Sesion.Rol})";

    public ShellEmpleadaViewModel(SesionUsuario sesion, CatalogoViewModel catalogo, Func<Task> cerrar)
    {
        Sesion = sesion;
        Catalogo = catalogo;
        CerrarSesion = cerrar;
    }
}
