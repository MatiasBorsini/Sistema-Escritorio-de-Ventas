using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Detalle de recepción para ventana modeless de solo lectura: referencia
/// humana, proveedor, fecha, responsable, stats y tabla completa con búsqueda
/// interna en memoria. Una sola lectura de líneas al abrir.
/// </summary>
public sealed class DetalleRecepcionViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _filtro = string.Empty;
    private string? _error;
    private bool _ocupado;
    private bool _cargado;

    public DetalleRecepcionViewModel(ServiciosComerciales servicios, Func<string?> idSesion, ResumenRecepcion resumen)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        Resumen = resumen;
        Lineas = new ObservableCollection<LineaRecepcionDetalle>();
        LineasFiltradas = new ObservableCollection<LineaRecepcionDetalle>();
    }

    public ResumenRecepcion Resumen { get; }
    public ObservableCollection<LineaRecepcionDetalle> Lineas { get; }
    public ObservableCollection<LineaRecepcionDetalle> LineasFiltradas { get; }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }

    public string TituloVentana => $"Detalle de recepción · {FilaHistorialRecepcion.ReferenciaDe(Resumen)}";
    public string SubtituloVentana => $"{Resumen.ProveedorNombre} · {FechaTexto}";
    public string FechaTexto => FilaHistorialRecepcion.FechaCorta(Resumen.CreadoEn);
    public long? TotalExacto => Resumen.LineasSinCosto == 0 ? Resumen.TotalMinor : null;

    public string Filtro
    {
        get => _filtro;
        set
        {
            if (SetProperty(ref _filtro, value))
                AplicarFiltro();
        }
    }

    public Task CargarAsyncPublic() => CargarAsync();

    public async Task CargarAsync()
    {
        if (Ocupado || _cargado) return;
        Error = null; Ocupado = true;
        try
        {
            var lineas = await _servicios.ConsultasRecepciones.ListarLineasDetalleAsync(Resumen.Id, _idSesion());
            Lineas.Clear();
            foreach (var l in lineas) Lineas.Add(l);
            _cargado = true;
            AplicarFiltro();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private void AplicarFiltro()
    {
        LineasFiltradas.Clear();
        var q = _filtro?.Trim().ToLowerInvariant() ?? string.Empty;
        foreach (var l in Lineas)
        {
            if (q.Length == 0 || l.Sku.ToLowerInvariant().Contains(q))
                LineasFiltradas.Add(l);
        }
    }

    private static string MensajeError(Exception ex) => ex switch
    {
        LocalInfantil.Application.Abstractions.AccesoDenegadoException => "Permiso denegado.",
        LocalInfantil.Application.Abstractions.SesionInvalidaException => "Sesión inválida. Iniciá sesión nuevamente.",
        _ => ex.Message,
    };
}
