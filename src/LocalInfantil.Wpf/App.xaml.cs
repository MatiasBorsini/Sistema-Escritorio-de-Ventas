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
    private IAsyncDisposable? _leaseInstalacionActivo;

    public void MantenerLeaseInstalacionHastaSalir(IAsyncDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (_leaseInstalacionActivo is not null)
            throw new InvalidOperationException("Ya hay una instalación en curso.");
        _leaseInstalacionActivo = lease;
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if(e.Args.Length>0&&string.Equals(e.Args[0],"--restore-swap",StringComparison.OrdinalIgnoreCase))
        {
            try{await IntercambiadorRestauracion.EjecutarHelperAsync(e.Args);}catch{ }
            Shutdown(0);return;
        }

        var inicio = ModoInicio.Resolver(e.Args);

        if (inicio.Destino == DestinoInicio.Error)
        {
            // Ruta rechazada: mostrar error sin tocar ninguna base
            var rootError = new AppRootViewModel(null, null, inicio.Error, new InicioViewModel());
            var winError = new MainWindow { DataContext = rootError };
            winError.Show();
            return;
        }

        if (inicio.Destino == DestinoInicio.Desarrollo && inicio.RutaBase is not null)
        {
            await IniciarModoComercialAsync(inicio.RutaBase, null, esProduccion: false);
            return;
        }

        if (inicio.Destino == DestinoInicio.PrototipoExplicito)
        {
            // Diagnóstico histórico explícito: sonda de solo prueba, sin migraciones.
            AbrirSondaPrototipo();
            return;
        }

        // Sin argumentos: modo PRODUCCIÓN (D-45). Jamás abre Prototype.
        if (inicio.RutaBase is not null)
        {
            await IniciarModoComercialAsync(inicio.RutaBase, null, esProduccion: true);
            return;
        }
    }

    private void AbrirSondaPrototipo()
    {
        // Diagnóstico histórico explícito (--prototype): conserva el
        // comportamiento anterior del prototipo intacto, sin migraciones.
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

    private async Task IniciarModoComercialAsync(string rutaBase, string? errorPrevio, bool esProduccion)
    {
        ServiciosComerciales? servicios = null;
        string? error = errorPrevio;
        ViewModelBase contenidoInicial = new InicioViewModel();
        var requiereBootstrap = false;

        try
        {
            servicios = new ServiciosComerciales(rutaBase);
            var migrador = new MigradorBaseDatos(rutaBase);
            // Producción usa la raíz real de respaldos (gestionada y retenida);
            // desarrollo usa una carpeta junto a la base ficticia.
            var respaldoDir = esProduccion
                ? RutaBaseDatosProductiva.ObtenerDirRespaldos()
                : Path.Combine(Path.GetDirectoryName(rutaBase) ?? ".", "respaldos-dev");
            var inicio = await new InicializadorArranque(servicios).InicializarAsync(respaldoDir);
            requiereBootstrap = inicio.RequiereBootstrapDuena;
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
            var rootError = new AppRootViewModel(null, rutaBase, error, contenidoInicial);
            var winError = new DevMainWindow { DataContext = rootError };
            winError.Show();
            return;
        }

        _serviciosActivo = servicios;

            var root = new AppRootViewModel(servicios, rutaBase, error, contenidoInicial, esProduccion);

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
                ventanaReingreso = new Views.LoginWindow(reingresoVm, rutaBase);
                ventanaReingreso.Closed += (_, _) =>
                {
                    if (!reautenticado)
                        Shutdown();
                };
                ventanaReingreso.Show();
            };
        }

        if (requiereBootstrap)
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
        ventanaLogin = new Views.LoginWindow(loginVm, rutaBase);
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
        if (_leaseInstalacionActivo is not null)
        {
            try { _leaseInstalacionActivo.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* La conexión se cierra al salir del proceso si SQLite falla. */ }
            _leaseInstalacionActivo = null;
        }
        base.OnExit(e);
    }
}

