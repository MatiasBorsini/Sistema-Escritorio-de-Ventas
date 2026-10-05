using System.IO;
using System.Windows;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Impresion;
using LocalInfantil.Application.Features.Persistencia;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Wpf.Infra;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiciosComerciales? _serviciosActivo;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if(e.Args.Length>0&&string.Equals(e.Args[0],"--restore-swap",StringComparison.OrdinalIgnoreCase))
        {
            try{await IntercambiadorRestauracion.EjecutarHelperAsync(e.Args);}catch{ }
            Shutdown(0);return;
        }

        string? rutaDev = null;
        string? errorDev = null;
        try
        {
            rutaDev = SelectorBaseDesarrollo.Resolver(e.Args);
        }
        catch (Exception ex)
        {
            errorDev = ex.Message;
        }

        if (rutaDev is not null)
        {
            await IniciarModoDesarrolloAsync(rutaDev, errorDev);
            return;
        }

        if (errorDev is not null)
        {
            // Provisional rechazada: mostrar error sin tocar esa base
            var rootError = new AppRootViewModel(null, null, errorDev, new InicioViewModel());
            var winError = new MainWindow { DataContext = rootError };
            // Mostrar mensaje informativo junto al prototipo existente
            winError.Show();
            return;
        }

        // Sin base de desarrollo explícita: conservar comportamiento prototipo intacto
        var mainWindow = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                new ComprobarPersistenciaLocal(
                    new SqlitePersistenciaTecnicaDePrueba(RutaBaseDatosProvisional.ObtenerPredeterminada())),
                new GenerarDocumentoImpresionDePrueba(),
                AbrirVistaPreviaImpresion)
        };

        mainWindow.Show();
    }

    private async Task IniciarModoDesarrolloAsync(string rutaDev, string? errorPrevio)
    {
        ServiciosComerciales? servicios = null;
        string? error = errorPrevio;
        ViewModelBase contenidoInicial = new InicioViewModel();

        try
        {
            servicios = new ServiciosComerciales(rutaDev);
            var migrador = new MigradorBaseDatos(rutaDev);
            var respaldoDir = Path.Combine(Path.GetDirectoryName(rutaDev) ?? ".", "respaldos-dev");
            Directory.CreateDirectory(respaldoDir);
            await migrador.MigrarAsync(MigracionesF01.Todas, respaldoDir);

            await using var sesion = await servicios.Fabrica.IniciarLecturaAsync();
            _ = await servicios.RepoUsuarios.ContarTodosAsync(sesion);
            EscribirEstadoRestauracion(Environment.GetCommandLineArgs(),"OK");
            await servicios.CoordinadorRespaldos.IniciarAsync();
        }
        catch (Exception ex)
        {
            error = (error is null ? string.Empty : error + " ") + ex.Message;
            servicios = null;
        }

        if (servicios is null)
        {
            EscribirEstadoRestauracion(Environment.GetCommandLineArgs(),"ERROR no se pudo abrir la base restaurada");
            var rootError = new AppRootViewModel(null, rutaDev, error, contenidoInicial);
            var winError = new DevMainWindow { DataContext = rootError };
            winError.Show();
            return;
        }

        _serviciosActivo = servicios;

        var root = new AppRootViewModel(servicios, rutaDev, error, contenidoInicial);

        // Cerrar sesión respeta la misma arquitectura que el inicio: Login
        // siempre en LoginWindow. Se oculta la principal, se abre una
        // LoginWindow nueva; si autentica, se vuelve a mostrar la principal;
        // si se cierra sin autenticar, se cierra la aplicación.
        void ConfigurarCierreSesion(DevMainWindow principal)
        {
            root.AlCerrarSesion = async () =>
            {
                principal.Hide();
                var reautenticado = false;
                Views.LoginWindow? ventanaReingreso = null;
                var reingresoVm = new LoginViewModel(servicios, async s =>
                {
                    reautenticado = true;
                    await root.AutenticadoAsync(s);
                    if (principal.WindowState == WindowState.Minimized)
                        principal.WindowState = WindowState.Normal;
                    principal.Show();
                    principal.Activate();
                    ventanaReingreso?.Close();
                });
                ventanaReingreso = new Views.LoginWindow(reingresoVm, rutaDev);
                ventanaReingreso.Closed += (_, _) =>
                {
                    if (!reautenticado)
                        Shutdown();
                };
                ventanaReingreso.Show();
            };
        }

        await using var sesionLectura = await servicios.Fabrica.IniciarLecturaAsync();
        var total = await servicios.RepoUsuarios.ContarTodosAsync(sesionLectura);
        if (total == 0)
        {
            // Bootstrap de primera dueña preservado: misma ventana principal.
            root.Mostrar(new BootstrapDuenaViewModel(servicios, async s => await root.AutenticadoAsync(s)));
            var winBootstrap = new DevMainWindow { DataContext = root };
            ConfigurarCierreSesion(winBootstrap);
            winBootstrap.Show();
            return;
        }

        // Ya existe configuración inicial: LoginWindow separada y compacta.
        // Login exitoso → cerrar LoginWindow y abrir la principal.
        var autenticado = false;
        Views.LoginWindow? ventanaLogin = null;
        var loginVm = new LoginViewModel(servicios, async s =>
        {
            autenticado = true;
            await root.AutenticadoAsync(s);
            var win = new DevMainWindow { DataContext = root };
            ConfigurarCierreSesion(win);
            win.Show();
            ventanaLogin?.Close();
        });
        ventanaLogin = new Views.LoginWindow(loginVm, rutaDev);
        ventanaLogin.Closed += (_, _) =>
        {
            if (!autenticado)
                Shutdown();
        };
        ventanaLogin.Show();
    }

    private static void AbrirVistaPreviaImpresion(DocumentoImpresionDePrueba documento)
    {
        var ventana = new Views.VistaPreviaImpresionWindow(documento)
        {
            Owner = Current.MainWindow
        };

        ventana.ShowDialog();
    }

    private static void EscribirEstadoRestauracion(string[] args,string value)
    {
        for(var i=0;i<args.Length-1;i++)if(string.Equals(args[i],"--restore-verify",StringComparison.OrdinalIgnoreCase))try{File.WriteAllText(args[i+1],value);}catch{ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviciosActivo is not null)
        {
            try
            {
                if (_serviciosActivo.CoordinadorRespaldos.DetenerAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult())
                    _serviciosActivo.CoordinadorRespaldos.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch { /* El paquete pendiente queda para reconciliar en el próximo inicio. */ }
        }
        base.OnExit(e);
    }
}

