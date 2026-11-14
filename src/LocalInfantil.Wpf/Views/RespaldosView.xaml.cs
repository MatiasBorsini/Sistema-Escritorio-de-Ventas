using System.Windows.Controls;
using System.Windows;
using System.Windows.Documents;
using System.ComponentModel;
using System.IO;

namespace LocalInfantil.Wpf.Views;

public partial class RespaldosView : UserControl
{
    public RespaldosView()
    {
        InitializeComponent();
        DataContextChanged += RespaldosView_DataContextChanged;
    }

    private void RespaldosView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged anterior)
            anterior.PropertyChanged -= ViewModel_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged actual)
            actual.PropertyChanged += ViewModel_PropertyChanged;
        ActualizarDisponibilidadRestaurar();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.RespaldosViewModel.ArchivoRestauracion)
            or nameof(ViewModels.RespaldosViewModel.CodigoRestauracion)
            or nameof(ViewModels.RespaldosViewModel.Ocupado)
            or null)
            ActualizarDisponibilidadRestaurar();
    }

    private void PasswordDuena_PasswordChanged(object sender, RoutedEventArgs e)
        => ActualizarDisponibilidadRestaurar();

    private void ActualizarDisponibilidadRestaurar()
    {
        if (RestaurarButton is null || PasswordDuena is null)
            return;

        if (DataContext is not ViewModels.RespaldosViewModel vm)
        {
            RestaurarButton.IsEnabled = false;
            return;
        }

        RestaurarButton.IsEnabled = vm.PuedeRestaurar
            && !vm.Ocupado
            && !string.IsNullOrWhiteSpace(vm.ArchivoRestauracion)
            && File.Exists(vm.ArchivoRestauracion)
            && !string.IsNullOrWhiteSpace(vm.CodigoRestauracion)
            && !string.IsNullOrWhiteSpace(PasswordDuena.Password);
    }

    private void Restaurar_Click(object sender,System.Windows.RoutedEventArgs e)
    {
        if(DataContext is ViewModels.RespaldosViewModel vm)
        {
            vm.ContrasenaDuena=PasswordDuena.Password;
            if(vm.RestaurarCommand.CanExecute(null))vm.RestaurarCommand.Execute(null);
            PasswordDuena.Clear();
            ActualizarDisponibilidadRestaurar();
        }
    }

    private void ImprimirHoja_Click(object sender,RoutedEventArgs e)
    {
        if(DataContext is not ViewModels.RespaldosViewModel vm||string.IsNullOrWhiteSpace(vm.Codigo))return;
        var dialog=new PrintDialog();if(dialog.ShowDialog()!=true)return;
        var document=new FlowDocument{PageWidth=793.7,PageHeight=1122.5,PagePadding=new Thickness(64),ColumnWidth=665};
        document.Blocks.Add(new Paragraph(new Run("LocalInfantil — Código de recuperación de respaldos")){FontSize=22,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,24)});
        document.Blocks.Add(new Paragraph(new Run(vm.Codigo)){FontSize=20,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,24)});
        document.Blocks.Add(new Paragraph(new Run("Necesitás este código si perdés esta computadora. Guardalo fuera del equipo. No lo envíes por correo ni lo guardes dentro de la carpeta de respaldos.")){FontSize=14,Margin=new Thickness(0,0,0,16)});
        document.Blocks.Add(new Paragraph(new Run("Recomendación: conservá dos copias físicas y al menos una fuera del local.")){FontSize=14});
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,"Código de recuperación LocalInfantil");
    }
}
