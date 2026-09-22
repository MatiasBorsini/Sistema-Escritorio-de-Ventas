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
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
            var total = await servicios.RepoUsuarios.ContarTodosAsync(sesion);
            if (total == 0)
            {
                // placeholder: se reemplaza tras crear root
                contenidoInicial = new InicioViewModel();
            }
            else
            {
                contenidoInicial = new InicioViewModel();
            }
        }
        catch (Exception ex)
        {
            error = (error is null ? string.Empty : error + " ") + ex.Message;
            servicios = null;
        }

        AppRootViewModel root;
        if (servicios is null)
        {
            root = new AppRootViewModel(null, rutaDev, error, contenidoInicial);
        }
        else
        {
            root = new AppRootViewModel(servicios, rutaDev, error, contenidoInicial);
            await using var sesion = await servicios.Fabrica.IniciarLecturaAsync();
            var total = await servicios.RepoUsuarios.ContarTodosAsync(sesion);
            if (total == 0)
            {
                root.Mostrar(new BootstrapDuenaViewModel(servicios, async s => await root.AutenticadoAsync(s)));
            }
            else
            {
                root.Mostrar(new LoginViewModel(servicios, async s => await root.AutenticadoAsync(s)));
            }
        }

        var win = new DevMainWindow { DataContext = root };
        win.Show();
    }

    private static void AbrirVistaPreviaImpresion(DocumentoImpresionDePrueba documento)
    {
        var ventana = new Views.VistaPreviaImpresionWindow(documento)
        {
            Owner = Current.MainWindow
        };

        ventana.ShowDialog();
    }
}

