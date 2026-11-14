using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Inicio;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class PortadaViewModel : ViewModelBase
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");
    private readonly Func<Task<ResumenInicio>>? _recargar;
    private readonly ICommand? _irAVentas;
    private readonly ICommand? _irAPos;
    private readonly ICommand? _irARecepciones;
    private readonly ICommand _recibir;
    private readonly ICommand _nuevo;
    private readonly ICommand _conteo;
    private readonly ICommand _inventario;
    private ResumenInventarioInicio _inventarioActual = new(0, 0, 0);
    private int _conteosPendientes;
    private VentasDelDiaInicio? _ventasDelDia;
    private RecepcionRecienteInicio? _recepcionReciente;
    private bool _ocupado;
    private string? _error;

    public PortadaViewModel(string nombre, ResumenInicio d, ICommand recibir, ICommand nuevo, ICommand conteo, ICommand inventario,
        Func<Task<ResumenInicio>>? recargar = null, ICommand? irAVentas = null, ICommand? irARecepciones = null, ICommand? vender = null)
    {
        NombreUsuario = nombre;
        _recibir = recibir;
        _nuevo = nuevo;
        _conteo = conteo;
        _inventario = inventario;
        _recargar = recargar;
        _irAVentas = irAVentas;
        _irARecepciones = irARecepciones;
        _irAPos = vender;
        IrAConteos = conteo;
        IrAInventario = inventario;
        Aplicar(d);
    }

    public string NombreUsuario { get; }
    public string Bienvenida => $"Buen día, {NombreUsuario}.";
    public string NotaRol => "Los accesos disponibles respetan los permisos definidos por la dueña.";
    public ObservableCollection<AccionInicio> Acciones { get; } = new();
    public ObservableCollection<IndicadorInicio> Indicadores { get; } = new();
    public ObservableCollection<FilaActividad> Actividad { get; } = new();

    public ResumenInventarioInicio Inventario => _inventarioActual;
    public int ConteosPendientes => _conteosPendientes;
    public string TextoConteosPendientes => ConteosPendientes == 1 ? "1 conteo espera revisión" : $"{ConteosPendientes} conteos esperan revisión";
    public bool HayConteosPendientes => ConteosPendientes > 0;
    public string TextoApartado => Inventario.Apartado == 1 ? "1 unidad está apartada" : $"{Inventario.Apartado} unidades están apartadas";
    public bool HayApartado => Inventario.Apartado > 0;
    public bool HayPendientes => HayConteosPendientes || HayApartado;
    public bool HayActividad => Actividad.Count > 0;

    public VentasDelDiaInicio? VentasDelDia => _ventasDelDia;
    public bool HayVentasDelDia => _ventasDelDia is not null;
    public RecepcionRecienteInicio? RecepcionReciente => _recepcionReciente;
    public bool HayRecepcionReciente => _recepcionReciente is not null;

    public bool Ocupado
    {
        get => _ocupado;
        private set => SetProperty(ref _ocupado, value);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public ICommand IrAConteos { get; }
    public ICommand IrAInventario { get; }

    /// <summary>Abre la consulta/historial de recepciones (sólo lectura).</summary>
    public ICommand IrARecepciones => _irARecepciones ?? new RelayCommand(_ => { });

    /// <summary>Vuelve a leer el resumen operativo sin bloquear la navegación.</summary>
    public async Task RefrescarAsync()
    {
        if (_recargar is null || Ocupado) return;
        Ocupado = true;
        Error = null;
        try
        {
            Aplicar(await _recargar());
        }
        catch
        {
            Error = "No se pudo actualizar el inicio. Se conservan los últimos datos conocidos.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Aplicar(ResumenInicio d)
    {
        _inventarioActual = d.Inventario;
        _conteosPendientes = d.ConteosPendientes;
        _ventasDelDia = d.VentasDelDia;
        _recepcionReciente = d.RecepcionReciente;

        Actividad.Clear();
        foreach (var actividad in d.Actividad) Actividad.Add(new FilaActividad(actividad, ClaveDeActividad(actividad.Descripcion)));

        Acciones.Clear();
        if (_irAPos is not null) Acciones.Add(new("Realizar venta", "ventas", _irAPos));
        if (d.AccionesPermitidas.Contains(ConsultasInicio.RecibirMercaderia)) Acciones.Add(new("Recepción de mercadería", "recepcion", _recibir));
        if (d.AccionesPermitidas.Contains(ConsultasInicio.NuevoProducto)) Acciones.Add(new("Nuevo producto", "producto", _nuevo));
        if (d.AccionesPermitidas.Contains(ConsultasInicio.ConteoFisico)) Acciones.Add(new("Conteo físico", "conteo", _conteo));
        Acciones.Add(new("Consultar inventario", "inventario", _inventario));

        Indicadores.Clear();
        if (d.VentasDelDia is { } ventas)
            Indicadores.Add(new("Ventas de hoy", Moneda(ventas.NetoMinor), $"{Operaciones(ventas.CantidadVentas)} · neto vendido",
                "No es efectivo en caja ni liquidación de Mercado Pago.", "ventas", _irAVentas));
        Indicadores.Add(new("Inventario disponible", d.Inventario.Disponible.ToString(EsAr),
            $"Físico {d.Inventario.Fisico} · Apartado {d.Inventario.Apartado}", "Físico = disponible + apartado.", "inventario", _inventario));
        Indicadores.Add(d.RecepcionReciente is { } recepcion
            ? new("Recepción más reciente", recepcion.ProveedorNombre, $"{Unidades(recepcion.Unidades)} · {Fecha(recepcion.CreadoEn)}",
                "Último ingreso registrado de mercadería.", "recepcion", _irARecepciones)
            : new("Recepción más reciente", "Sin recepciones", "Todavía no se registró mercadería.", "Al confirmar una recepción aparece acá.", "recepcion", _irARecepciones));

        NotificarResumen();
    }

    private void NotificarResumen()
    {
        OnPropertyChanged(nameof(Inventario));
        OnPropertyChanged(nameof(ConteosPendientes));
        OnPropertyChanged(nameof(TextoConteosPendientes));
        OnPropertyChanged(nameof(HayConteosPendientes));
        OnPropertyChanged(nameof(TextoApartado));
        OnPropertyChanged(nameof(HayApartado));
        OnPropertyChanged(nameof(HayPendientes));
        OnPropertyChanged(nameof(HayActividad));
        OnPropertyChanged(nameof(VentasDelDia));
        OnPropertyChanged(nameof(HayVentasDelDia));
        OnPropertyChanged(nameof(RecepcionReciente));
        OnPropertyChanged(nameof(HayRecepcionReciente));
    }

    private static string ClaveDeActividad(string descripcion) => descripcion switch
    {
        var d when d.StartsWith("Recepción", StringComparison.Ordinal) => "recepcion",
        var d when d.StartsWith("Conteo", StringComparison.Ordinal) => "conteo",
        var d when d.StartsWith("Baja definitiva", StringComparison.Ordinal) => "baja",
        var d when d.StartsWith("Mercadería reincorporada", StringComparison.Ordinal) => "reincorporado",
        var d when d.StartsWith("Mercadería", StringComparison.Ordinal) => "apartado",
        _ => "otro",
    };

    private static string Moneda(long minor) => $"ARS {(minor / 100m).ToString("N2", EsAr)}";
    private static string Operaciones(int cantidad) => cantidad == 1 ? "1 operación" : $"{cantidad} operaciones";
    private static string Unidades(long cantidad) => cantidad == 1 ? "1 unidad" : $"{cantidad} unidades";
    private static string Fecha(string creadoEn)
        => DateTimeOffset.TryParse(creadoEn, out var valor) ? valor.ToLocalTime().ToString("dd/MM HH:mm") : creadoEn;
}

    /// <summary>Fila ya preparada para la vista: marca el tipo con una clave de icono.</summary>
    public sealed record FilaActividad(ActividadInicio Evento, string Clave)
    {
        public string Descripcion => Evento.Descripcion;
        public string Detalle => Evento.Detalle;
        public bool HayDetalle => Evento.Detalle.Length > 0;
        public string ResponsableYFecha => Evento.ResponsableYFecha;
    }

    public sealed record AccionInicio(string Titulo, string Clave, ICommand Comando);

    public sealed record IndicadorInicio(string Titulo, string Valor, string Detalle, string Nota, string Clave, ICommand? Comando);
