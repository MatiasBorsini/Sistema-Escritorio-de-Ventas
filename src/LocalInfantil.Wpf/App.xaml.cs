using System.Configuration;
using System.Data;
using System.Windows;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Impresion;
using LocalInfantil.Application.Features.Persistencia;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Wpf.ViewModels;

namespace LocalInfantil.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    private static void AbrirVistaPreviaImpresion(DocumentoImpresionDePrueba documento)
    {
        var ventana = new Views.VistaPreviaImpresionWindow(documento)
        {
            Owner = Current.MainWindow
        };

        ventana.ShowDialog();
    }
}

