using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class FilaLineaCambio(LineaCambioDisponible item) : ViewModelBase
{
    public LineaCambioDisponible Item { get; } = item;
    private long _cantidad;
    private bool _apta = true;
    private string _motivo = string.Empty;
    public long Cantidad
    {
        get => _cantidad;
        set
        {
            if (!SetProperty(ref _cantidad, value)) return;
            OnPropertyChanged(nameof(ImporteMinor));
            OnPropertyChanged(nameof(CantidadExcedeDisponible));
            OnPropertyChanged(nameof(ErrorCantidad));
        }
    }
    public bool CantidadExcedeDisponible => Cantidad > Item.CantidadDisponible;
    public string ErrorCantidad => CantidadExcedeDisponible
        ? $"Supera el máximo: quedan {Item.CantidadDisponible} unidades por devolver."
        : string.Empty;
    public bool Apta { get => _apta; set => SetProperty(ref _apta, value); }
    public string Motivo { get => _motivo; set => SetProperty(ref _motivo, value); }
    public string Descripcion => $"{Item.Linea.NombreProducto} · {Item.Linea.CodigoInterno} · {Item.Linea.Talle ?? "—"} / {Item.Linea.Color ?? "—"}";
    public long ImporteMinor => Cantidad * Item.Linea.PrecioUnitarioMinor;
}

public sealed class FilaSalidaCambio(OpcionVarianteCambio opcion,long cantidad) : ViewModelBase
{
    public OpcionVarianteCambio Opcion { get; private set; } = opcion;
    private long _cantidad = cantidad;
    public long Cantidad { get => _cantidad; set { if(SetProperty(ref _cantidad,value)) OnPropertyChanged(nameof(ImporteMinor)); } }
    public long ImporteMinor => Cantidad*Opcion.PrecioMinor;
    public string Descripcion => $"{Opcion.Nombre} · {Opcion.CodigoInterno} · {Opcion.Talle??"—"} / {Opcion.Color??"—"}";
    public void ActualizarPrecio(long precio){Opcion=Opcion with{PrecioMinor=precio};OnPropertyChanged(nameof(Opcion));OnPropertyChanged(nameof(ImporteMinor));}
}

public sealed class FilaPagoCambio : ViewModelBase
{
    private string _medio=MediosPago.Efectivo;
    private string _importeTexto=string.Empty;
    private string _recibidoTexto=string.Empty;
    public string Medio { get=>_medio; set { if(SetProperty(ref _medio,value)) { OnPropertyChanged(nameof(EsEfectivo)); OnPropertyChanged(nameof(RecibidoMinor)); } } }
    public string ImporteTexto { get=>_importeTexto; set { if(SetProperty(ref _importeTexto,value)) OnPropertyChanged(nameof(ImporteMinor)); } }
    public string RecibidoTexto { get=>_recibidoTexto; set { if(SetProperty(ref _recibidoTexto,value)) OnPropertyChanged(nameof(RecibidoMinor)); } }
    public bool EsEfectivo => Medio==MediosPago.Efectivo;
    public long ImporteMinor => FilaPagoViewModel.TryParseArs(ImporteTexto,out var importe) ? importe : 0;
    public long? RecibidoMinor => EsEfectivo && FilaPagoViewModel.TryParseArs(RecibidoTexto,out var recibido) ? recibido : null;
    public bool ImporteValido => FilaPagoViewModel.TryParseArs(ImporteTexto,out var importe) && importe>0;
}

public sealed class FilaVentaCambio : ViewModelBase
{
    public FilaVentaCambio(ResumenVenta resumen) => Resumen = resumen;
    public ResumenVenta Resumen { get; private set; }
    public string Numero => Resumen.Numero;
    public long TotalMinor => Resumen.TotalMinor;
    public string TotalTexto => PosViewModel.FormatearArs(Resumen.TotalMinor);
    public bool Anulada => Resumen.Anulada;
    public string EstadoTexto => Resumen.Anulada ? "Anulada" : "Confirmada";
    public int CantidadLineas => Resumen.CantidadLineas;
    public string FechaTexto => FilaHistorialVenta.FechaCorta(Resumen.CreadaEn);
    public string AntiguedadTexto
    {
        get
        {
            if(!DateTimeOffset.TryParse(Resumen.CreadaEn,out var fecha)) return string.Empty;
            var dias=Math.Max(0,(int)(DateTimeOffset.Now.Date-fecha.ToLocalTime().Date).TotalDays);
            return dias==0?"Hoy":$"Hace {dias} días";
        }
    }

    /// <summary>Refresca el resumen preservando la identidad de la fila (y la
    /// selección de la vista) tras una búsqueda reactiva.</summary>
    public void Actualizar(ResumenVenta resumen)
    {
        if(ReferenceEquals(Resumen,resumen) || Resumen==resumen) return;
        Resumen=resumen;
        OnPropertyChanged(nameof(Resumen)); OnPropertyChanged(nameof(Numero)); OnPropertyChanged(nameof(TotalMinor));
        OnPropertyChanged(nameof(TotalTexto)); OnPropertyChanged(nameof(Anulada)); OnPropertyChanged(nameof(EstadoTexto));
        OnPropertyChanged(nameof(CantidadLineas)); OnPropertyChanged(nameof(FechaTexto)); OnPropertyChanged(nameof(AntiguedadTexto));
    }
}


public sealed class CambiosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _s; private readonly Func<string?> _sesion; private readonly bool _puedeConsultar; private readonly bool _puedeRegistrar;
    private FilaVentaCambio? _venta; private string _filtro=string.Empty; private string? _error; private string? _mensaje; private string _codigoSalida=string.Empty; private long _cantidadSalida=1; private string? _medioReintegro; private string _motivo=string.Empty; private string _usuarioAutorizante=string.Empty; private bool _contrasenaAutorizanteIngresada; private bool _ocupado; private string? _cambioIdReciente; private DateTime? _desde; private DateTime? _hasta; private string? _operacionPendiente; private string _seccionActiva="operacion"; private string _modoOperacion="cambio";
    private sealed record ReintegroPendiente(long Importe,bool VerificacionPendiente=false);
    private readonly Dictionary<string,ReintegroPendiente> _reintegrosMpPendientes=new(StringComparer.Ordinal);
    private readonly Dictionary<string,FilaVentaCambio> _ventasPorId=new(StringComparer.Ordinal);
    private CancellationTokenSource? _ctsVentas; private long _secuenciaVentas; private bool _buscandoVentas;
    private long _secuenciaDetalle; private bool _cargandoDetalle; private SugerenciaVariante? _articuloSeleccionado;
    public CambiosViewModel(ServiciosComerciales s,Func<string?> sesion,bool puedeConsultar,bool puedeRegistrar)
    {
        _s=s;_sesion=sesion;_puedeConsultar=puedeConsultar;_puedeRegistrar=puedeRegistrar;
        BuscarCommand=new AsyncRelayCommand(BuscarAsyncPublic); AgregarSalidaCommand=new AsyncRelayCommand(_=>AgregarSalidaDesdeSeleccionAsync()); CargarCommand=new AsyncRelayCommand(CargarAsync); AgregarPagoCommand=new RelayCommand(_=>AgregarPago()); QuitarPagoCommand=new RelayCommand(p=>QuitarPago(p as FilaPagoCambio)); QuitarSalidaCommand=new RelayCommand(p=>QuitarSalida(p as FilaSalidaCambio));
        ReembolsarMpCommand=new AsyncRelayCommand(ReembolsarMpAsync);
        MotorArticulos=new MotorSugerencias<SugerenciaVariante>((t,ct)=>Task.Run(()=>_s.ConsultasCambios.BuscarArticulosNuevosAsync(t,10,_sesion(),ct),ct),seleccionarPrimeraAlBuscar:false);
        MotorArticulos.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MotorSugerencias<SugerenciaVariante>.Texto))InvalidarArticuloSeleccionado();};
        SeleccionarArticuloCommand=new RelayCommand(p=>SeleccionarArticulo(p as SugerenciaVariante));
        AgregarSalidaDirectaCommand=new AsyncRelayCommand(p=>AgregarSalidaDirectaAsync(p as string));
        Entradas.CollectionChanged+=(_,e)=>{if(e.OldItems is not null)foreach(FilaLineaCambio fila in e.OldItems)fila.PropertyChanged-=FilaOperacion_PropertyChanged;if(e.NewItems is not null)foreach(FilaLineaCambio fila in e.NewItems)fila.PropertyChanged+=FilaOperacion_PropertyChanged;ActualizarTotales();};
        Salidas.CollectionChanged+=(_,e)=>{if(e.OldItems is not null)foreach(FilaSalidaCambio fila in e.OldItems)fila.PropertyChanged-=FilaOperacion_PropertyChanged;if(e.NewItems is not null)foreach(FilaSalidaCambio fila in e.NewItems)fila.PropertyChanged+=FilaOperacion_PropertyChanged;ActualizarTotales();};
        Pagos.CollectionChanged+=(_,e)=>{if(e.OldItems is not null)foreach(FilaPagoCambio fila in e.OldItems)fila.PropertyChanged-=FilaOperacion_PropertyChanged;if(e.NewItems is not null)foreach(FilaPagoCambio fila in e.NewItems)fila.PropertyChanged+=FilaOperacion_PropertyChanged;OnPropertyChanged(nameof(PuedeQuitarPago));OnPropertyChanged(nameof(TieneBorradorOperacion));ActualizarTotales();};
        Historial.CollectionChanged+=(_,_)=>OnPropertyChanged(nameof(HistorialVacio));
        Pagos.Add(NuevaFilaPago());
    }
    public ObservableCollection<FilaVentaCambio> Ventas {get;}=[];
    public ObservableCollection<FilaLineaCambio> Entradas {get;}=[];
    public ObservableCollection<FilaSalidaCambio> Salidas {get;}=[];
    public ObservableCollection<FilaPagoCambio> Pagos {get;}=[];
    public ObservableCollection<ResumenCambio> Historial {get;}=[];
    public IReadOnlyList<PresentacionMedioPagoPos> Medios => PresentacionMedioPagoPos.Todas.Where(x=>x.Valor!=MediosPago.MercadoPago).ToArray();
    public string[] MediosReintegro {get;}=[MediosPago.Efectivo,MediosPago.Transferencia,MediosPago.Debito,MediosPago.Credito,MediosPago.Billetera,MediosPago.MercadoPago];
    public bool VentaTieneMp {get=>_ventaTieneMp;private set{if(SetProperty(ref _ventaTieneMp,value))OnPropertyChanged(nameof(AvisoReintegroMpVisible));}}
    private bool _ventaTieneMp;
    public bool AvisoReintegroMpVisible => Diferencia<0 && VentaTieneMp && MedioReintegro!=MediosPago.MercadoPago;
    public string AvisoReintegroMp => "El pago original de Mercado Pago no será reembolsado automáticamente.";
    public bool AvisoReembolsoMpSeparadoVisible => Diferencia<0 && MedioReintegro==MediosPago.MercadoPago;
    public bool PuedeReembolsarMp => Venta is not null && _reintegrosMpPendientes.TryGetValue(Venta.Resumen.Id,out var pendiente) && !pendiente.VerificacionPendiente && !Ocupado;
    public bool ReintegroMpPendienteVisible => Venta is not null && _reintegrosMpPendientes.ContainsKey(Venta.Resumen.Id);
    public string ReintegroMpPendienteTexto => Venta is not null && _reintegrosMpPendientes.TryGetValue(Venta.Resumen.Id,out var pendiente)
        ? pendiente.VerificacionPendiente
            ? $"Pendiente de verificar: no repitas el reintegro de {PosViewModel.FormatearArs(pendiente.Importe)} por Mercado Pago."
            : $"Cambio registrado. Queda pendiente devolver {PosViewModel.FormatearArs(pendiente.Importe)} por Mercado Pago."
        : string.Empty;
    public AsyncRelayCommand ReembolsarMpCommand {get;}
    public MotorSugerencias<SugerenciaVariante> MotorArticulos {get;}
    public RelayCommand SeleccionarArticuloCommand {get;}
    public AsyncRelayCommand AgregarSalidaDirectaCommand {get;}
    public Task BuscarAsyncPublic(){return EjecutarBusquedaVentasAsync(null);}
    public bool BuscandoVentas {get=>_buscandoVentas;private set=>SetProperty(ref _buscandoVentas,value);}
    public string Filtro {get=>_filtro;set{if(SetProperty(ref _filtro,value))ProgramarBusquedaVentas();}}
    public FilaVentaCambio? Venta
    {
        get=>_venta;
        set
        {
            if(ReferenceEquals(_venta,value)) return;
            if(Ocupado) { OnPropertyChanged(); return; }
            _venta=value;
            OnPropertyChanged();
            LimpiarOperacionDeVenta();
            OnPropertyChanged(nameof(PuedeEditarOperacion));
            OnPropertyChanged(nameof(PuedeContinuarOperacion));
            OnPropertyChanged(nameof(PuedeRegistrarOperacion));
            OnPropertyChanged(nameof(PuedeQuitarPago));
            OnPropertyChanged(nameof(PuedeSeleccionarVenta));
            OnPropertyChanged(nameof(VentaContexto));
            OnPropertyChanged(nameof(ResumenLineaTexto));
            OnPropertyChanged(nameof(VentaAnulada));
            OnPropertyChanged(nameof(SinVentaSeleccionada));
            OnPropertyChanged(nameof(MostrarEstadoVacioOperacion));
            OnPropertyChanged(nameof(EstadoVacioOperacion));
            OnPropertyChanged(nameof(MostrarOperacion));
            OnPropertyChanged(nameof(CargandoDetalle));
            OnPropertyChanged(nameof(PuedeAgregarArticulo));
            OnPropertyChanged(nameof(ReintegroMpPendienteVisible));
            OnPropertyChanged(nameof(ReintegroMpPendienteTexto));
            OnPropertyChanged(nameof(PuedeReembolsarMp));
            if(value is not null) _=CargarAsync();
        }
    }
    public string VentaContexto => Venta is null ? string.Empty : $"{Venta.Resumen.Numero} · {Venta.FechaTexto} · {Venta.Resumen.CantidadLineas} {(Venta.Resumen.CantidadLineas==1?"artículo":"artículos")} · {PosViewModel.FormatearArs(Venta.Resumen.TotalMinor)}";
    public string ResumenLineaTexto => Venta is null ? string.Empty : $"{Venta.FechaTexto} · {Venta.Resumen.CantidadLineas} {(Venta.Resumen.CantidadLineas==1?"artículo":"artículos")}";
    public bool VentaAnulada => Venta?.Resumen.Anulada == true;
    public bool SinVentaSeleccionada => Venta is null;
    public string? Error {get=>_error;set=>SetProperty(ref _error,value);}
    public string? Mensaje {get=>_mensaje;set=>SetProperty(ref _mensaje,value);}
    public bool Ocupado {get=>_ocupado;set{if(SetProperty(ref _ocupado,value)){OnPropertyChanged(nameof(PuedeEditarOperacion));OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(PuedeQuitarPago));OnPropertyChanged(nameof(PuedeSeleccionarVenta));OnPropertyChanged(nameof(PuedeReembolsarMp));OnPropertyChanged(nameof(PuedeAgregarArticulo));}}}
    public DateTime? Desde {get=>_desde;set{if(SetProperty(ref _desde,value))ProgramarBusquedaVentas();}}
    public DateTime? Hasta {get=>_hasta;set{if(SetProperty(ref _hasta,value))ProgramarBusquedaVentas();}}
    public bool PuedeConsultar=>_puedeConsultar;
    public bool PuedeRegistrar=>_puedeRegistrar;
    public bool PuedeSeleccionarVenta=>PuedeConsultar && !Ocupado;
    public bool PuedeEditarOperacion=>PuedeRegistrar && Venta is { Resumen.Anulada:false } && !Ocupado;
    public bool CargandoDetalle{get=>_cargandoDetalle;private set{if(SetProperty(ref _cargandoDetalle,value)){OnPropertyChanged(nameof(MostrarOperacion));OnPropertyChanged(nameof(PuedeEditarOperacion));OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(PuedeQuitarPago));OnPropertyChanged(nameof(PuedeAgregarArticulo));}}}
    public bool MostrarOperacion=>Venta is { Resumen.Anulada:false } && !CargandoDetalle;
    public SugerenciaVariante? ArticuloSeleccionado=>_articuloSeleccionado;
    public bool TieneArticuloSeleccionado=>_articuloSeleccionado is not null;
    public string TextoArticuloPresentacion=>_articuloSeleccionado is { } s ? $"{s.NombreProducto} · {s.CodigoInterno} · {s.Talle??"—"} / {s.Color??"—"}" : string.Empty;
    public string ArticuloSeleccionadoDetalle=>_articuloSeleccionado is { } s
        ? string.Join(" · ", new[] { s.StockTexto, s.TienePrecio ? PosViewModel.FormatearArs(s.PrecioMinor!.Value) : null }.Where(x => !string.IsNullOrWhiteSpace(x)))
        : string.Empty;
    public bool PuedeAgregarArticulo=>PuedeEditarOperacion && _articuloSeleccionado is { } s && CantidadSalida>0 && (s.PrecioMinor??0)>0 && (s.Disponible??0)>=CantidadSalida;
    public bool PuedeQuitarPago=>PuedeEditarOperacion && Pagos.Count>1;
    public bool PuedeContinuarOperacion => PuedeEditarOperacion && Entradas.Any(x=>x.Cantidad>0)
        && Entradas.All(x=>x.Cantidad>=0 && x.Cantidad<=x.Item.CantidadDisponible && (x.Cantidad==0 || x.Apta || !string.IsNullOrWhiteSpace(x.Motivo)))
        && Salidas.All(x=>x.Cantidad>0 && x.Cantidad<=x.Opcion.Disponible);
    public bool PuedeRegistrarOperacion=>PuedeEditarOperacion && Entradas.Any(x=>x.Cantidad>0)
        && Entradas.All(x=>x.Cantidad>=0 && x.Cantidad<=x.Item.CantidadDisponible && (x.Cantidad==0 || x.Apta || !string.IsNullOrWhiteSpace(x.Motivo)))
        && Salidas.All(x=>x.Cantidad>0 && x.Cantidad<=x.Opcion.Disponible)
        && (Diferencia>0 ? Pagos.Count>0 && Pagos.All(p=>p.ImporteValido) && Pagos.Sum(p=>p.ImporteMinor)==Diferencia && Pagos.All(p=>!p.EsEfectivo || p.RecibidoMinor>=p.ImporteMinor)
            : Diferencia<0 ? !string.IsNullOrWhiteSpace(Motivo) && (!RequiereAutorizacionPresencial || (!string.IsNullOrWhiteSpace(UsuarioAutorizante) && ContrasenaAutorizanteIngresada)) : true);
    public string ErrorCantidadesTexto
    {
        get
        {
            if (Entradas.Count == 0) return string.Empty;
            var excedidas = Entradas.Where(x => x.CantidadExcedeDisponible).ToList();
            if (excedidas.Count == 0) return string.Empty;
            return excedidas.Count == 1 ? $"{excedidas[0].Descripcion}: {excedidas[0].ErrorCantidad}"
                : $"{excedidas.Count} prendas superan el máximo: revisá las cantidades de las filas resaltadas.";
        }
    }
    public string ModoOperacion
    {
        get=>_modoOperacion;
        set
        {
            if(value=="devolucion" && (Salidas.Count>0 || !string.IsNullOrWhiteSpace(CodigoSalida)))
            {
                Error="Quitá los artículos nuevos antes de cambiar a Devolución.";
                OnPropertyChanged(nameof(EsModoCambio)); OnPropertyChanged(nameof(EsModoDevolucion));
                return;
            }
            if(value is not ("cambio" or "devolucion") || !SetProperty(ref _modoOperacion,value)) return;
            Error=null;
            OnPropertyChanged(nameof(EsModoCambio)); OnPropertyChanged(nameof(EsModoDevolucion));
            OnPropertyChanged(nameof(TextoConfirmarOperacion));
        }
    }
    public bool EsModoCambio {get=>ModoOperacion=="cambio";set{if(value)ModoOperacion="cambio";}}
    public bool EsModoDevolucion {get=>ModoOperacion=="devolucion";set{if(value)ModoOperacion="devolucion";}}
    public string TextoConfirmarOperacion=>EsModoDevolucion?"Registrar devolución":"Registrar cambio";
    public bool RequiereAutorizacionPresencial => Diferencia < 0 && MedioReintegro is not null && MedioReintegro != MediosPago.Efectivo && _s.Gestor.Obtener(_sesion() ?? string.Empty)?.Rol != "duena";
    public System.Windows.Input.ICommand AgregarPagoCommand{get;}
    public string CodigoSalida {get=>_codigoSalida;set{if(SetProperty(ref _codigoSalida,value))OnPropertyChanged(nameof(TieneBorradorOperacion));}}
    public long CantidadSalida {get=>_cantidadSalida;set{if(SetProperty(ref _cantidadSalida,value))OnPropertyChanged(nameof(PuedeAgregarArticulo));}}
    public string? MedioReintegro {get=>_medioReintegro;set{if(SetProperty(ref _medioReintegro,value)){OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}}
    public string Motivo {get=>_motivo;set{if(SetProperty(ref _motivo,value)){OnPropertyChanged(nameof(ErrorMotivo));OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(TieneBorradorOperacion));}}}
    public string UsuarioAutorizante {get=>_usuarioAutorizante;set{if(SetProperty(ref _usuarioAutorizante,value)){OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}}
    public bool ContrasenaAutorizanteIngresada {get=>_contrasenaAutorizanteIngresada;set{if(SetProperty(ref _contrasenaAutorizanteIngresada,value)){OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}}
    public string? CambioIdReciente {get=>_cambioIdReciente;private set=>SetProperty(ref _cambioIdReciente,value);}
    public long TotalEntradas=>Entradas.Sum(x=>x.ImporteMinor);
    public long TotalSalidas=>Salidas.Sum(x=>x.ImporteMinor);
    public long Diferencia=>TotalSalidas-TotalEntradas;
    public bool DiferenciaPositiva=>Diferencia>0;
    public bool DiferenciaCero=>Diferencia==0;
    public bool DiferenciaNegativa=>Diferencia<0;
    public string CreditoEntradasTexto=>PosViewModel.FormatearArs(TotalEntradas);
    public string TotalSalidasTexto=>PosViewModel.FormatearArs(TotalSalidas);
    public string ResultadoDiferenciaTexto=>!Entradas.Any(x=>x.Cantidad>0)&&Salidas.Count==0?"Seleccioná las prendas que vuelven":Diferencia>0?$"A cobrar {PosViewModel.FormatearArs(Diferencia)}":Diferencia<0?$"A reintegrar {PosViewModel.FormatearArs(-Diferencia)}":"Sin diferencia";
    public bool TieneBorradorOperacion=>Entradas.Any(x=>x.Cantidad>0)||Salidas.Count>0||!string.IsNullOrWhiteSpace(CodigoSalida)||!string.IsNullOrWhiteSpace(Motivo)||Pagos.Any(x=>!string.IsNullOrWhiteSpace(x.ImporteTexto)||!string.IsNullOrWhiteSpace(x.RecibidoTexto))||MedioReintegro is not null;
    public bool MuestraAvisoSinDiferencia=>DiferenciaCero && (Entradas.Any(x=>x.Cantidad>0)||Salidas.Count>0);
    public string EstadoVacioOperacion=>Venta is null?"Seleccioná una venta para iniciar un cambio.":VentaAnulada?"Esta venta está anulada y no admite cambios.":"Seleccioná las prendas que vuelven y agregá los artículos nuevos.";
    public bool MostrarEstadoVacioOperacion=>Venta is null || VentaAnulada;
    public bool HistorialVacio=>Historial.Count==0;
    public string SeccionActiva {get=>_seccionActiva;set{if(SetProperty(ref _seccionActiva,value)) OnPropertyChanged(nameof(MostrarHistorial));}}
    public bool MostrarHistorial=>SeccionActiva=="historial";
    public string ErrorMotivo=>Diferencia<0&&string.IsNullOrWhiteSpace(Motivo)?"Ingresá el motivo del reintegro.":string.Empty;
    public string ErrorCobro=>Diferencia<=0?string.Empty:Pagos.Count==0?"Agregá un medio de pago.":Pagos.Any(p=>!p.ImporteValido)?"Revisá el importe de cada medio.":Pagos.Any(p=>p.EsEfectivo&&(!p.RecibidoMinor.HasValue||p.RecibidoMinor<p.ImporteMinor))?"El efectivo recibido debe cubrir el importe.":Pagos.Sum(p=>p.ImporteMinor)!=Diferencia?"Los medios de pago deben cubrir exactamente la diferencia.":string.Empty;
    public AsyncRelayCommand BuscarCommand{get;} public AsyncRelayCommand CargarCommand{get;} public AsyncRelayCommand AgregarSalidaCommand{get;} public RelayCommand QuitarSalidaCommand{get;} public RelayCommand QuitarPagoCommand{get;}
    public async Task<DocumentoTicketCambio?> ObtenerTicketAsync(string idCambio)=>await _s.CasoGenerarTicketCambio.EjecutarAsync(idCambio,_sesion());
    public async Task<ResultadoCambio?> ConfirmarAsync(string? contrasenaAutorizante=null, DecisionCuentaCorrienteCambio? decisionCuenta=null, bool confirmarDecisionCuenta=false, long? deudaEsperadaMinor=null)
    {
            Error=null;Mensaje=null; if(Venta is null){Error="Seleccioná una venta.";return null;} if(VentaAnulada){Error="Esta venta está anulada y no admite cambios.";return null;}
        try
        {
            var entradas=Entradas.Where(x=>x.Cantidad>0).Select(x=>new LineaCambioEntrada(x.Item.Linea.Id,x.Cantidad,x.Apta,x.Apta?null:x.Motivo)).ToArray();
            var salidas=Salidas.Select(x=>new LineaCambioSalida(x.Opcion.VarianteId,x.Cantidad,x.Opcion.PrecioMinor)).ToArray();
            var cobros=Diferencia>0?Pagos.Where(p=>p.ImporteMinor>0).Select(p=>new PagoDiferenciaCambio(p.Medio,p.ImporteMinor,p.Medio==MediosPago.Efectivo?p.RecibidoMinor:null)).ToArray():Array.Empty<PagoDiferenciaCambio>();
            _operacionPendiente ??= _s.GeneradorIds.NuevoId();
            var datos=new DatosCambio(Venta.Resumen.Id,entradas,salidas,cobros,MedioReintegro,string.IsNullOrWhiteSpace(Motivo)?null:Motivo,_operacionPendiente,_sesion(),Diferencia<0&&MedioReintegro is not null&&MedioReintegro!=MediosPago.Efectivo?UsuarioAutorizante:null,contrasenaAutorizante,decisionCuenta,confirmarDecisionCuenta,deudaEsperadaMinor);
            var diferenciaTexto=ResultadoDiferenciaTexto;Ocupado=true; var result=await _s.CasoRegistrarCambio.EjecutarAsync(datos);
            if(result is ResultadoCambio.PrecioCambiado cambioPrecio)
            {
                foreach(var item in cambioPrecio.Cambios)
                {
                    var fila=Salidas.FirstOrDefault(x=>x.Opcion.VarianteId==item.VarianteId);
                    if(fila is not null) fila.ActualizarPrecio(item.PrecioVigenteMinor);
                }
                ActualizarTotales();
                var primero=cambioPrecio.Cambios[0];
                Error=$"El precio de {primero.NombreProducto} cambió de {PosViewModel.FormatearArs(primero.PrecioEsperadoMinor)} a {PosViewModel.FormatearArs(primero.PrecioVigenteMinor)}. Revisá el cambio antes de continuar.";
                return result;
            }
            if (result is ResultadoCambio.DecisionCuentaRequerida or ResultadoCambio.ConfirmacionCuenta) return result;
            var confirmado=(ResultadoCambio.Registrado)result;CambioIdReciente=confirmado.CambioId;Mensaje=$"Cambio {confirmado.Numero} registrado: {diferenciaTexto}";
            _s.NotificadorCambios.Publicar(this, AreaDatos.Cambios, AreaDatos.Cambios, AreaDatos.Stock, AreaDatos.Caja, AreaDatos.Ventas, AreaDatos.CuentaCorriente);
            if(Diferencia<0 && MedioReintegro==MediosPago.MercadoPago && Venta is not null)
            {
                _reintegrosMpPendientes[Venta.Resumen.Id]=new(-Diferencia);
                Mensaje+=". Crédito reconocido localmente; el reembolso remoto queda pendiente.";
                OnPropertyChanged(nameof(PuedeReembolsarMp));OnPropertyChanged(nameof(ReintegroMpPendienteVisible));OnPropertyChanged(nameof(ReintegroMpPendienteTexto));
            }
            _operacionPendiente = null;
            LimpiarCamposOperacionRegistrada();
            await CargarAsync();
            return result;
        }
        catch(Exception ex){Error=ex.Message; return null;}
        finally{Ocupado=false;}
    }
    private void ProgramarBusquedaVentas()
    {
        _ctsVentas?.Cancel();
        _ctsVentas?.Dispose();
        var cts=new CancellationTokenSource();
        _ctsVentas=cts;
        var token=cts.Token;
        _=EjecutarConDebounceAsync(token);
    }
    private async Task EjecutarConDebounceAsync(CancellationToken token)
    {
        try{ await Task.Delay(275,token); }catch(OperationCanceledException){ return; }
        await EjecutarBusquedaVentasAsync(token);
    }
    private async Task EjecutarBusquedaVentasAsync(CancellationToken? token)
    {
        Error=null;
        if(Desde.HasValue&&Hasta.HasValue&&Hasta.Value.Date<Desde.Value.Date){Error="La fecha hasta no puede ser anterior a la fecha desde.";return;}
        var secuencia=++_secuenciaVentas;
        var desde=Desde?.ToString("yyyy-MM-dd"); var hasta=Hasta?.ToString("yyyy-MM-dd"); var filtro=Filtro;
        BuscandoVentas=true;
        try
        {
            var result=await Task.Run(()=>_s.ConsultasCambios.BuscarVentasAsync(filtro,desde,hasta,_sesion()),token??CancellationToken.None);
            if(secuencia!=_secuenciaVentas||(token?.IsCancellationRequested??false))return;
            ReconciliarVentas(result);
        }
        catch(OperationCanceledException){ }
        catch(Exception ex){ if(secuencia==_secuenciaVentas) Error=ex.Message; }
        finally{ if(secuencia==_secuenciaVentas) BuscandoVentas=false; }
    }
    private void ReconciliarVentas(IReadOnlyList<ResumenVenta> resultados)
    {
        Ventas.Clear();
        var vistos=new HashSet<string>(StringComparer.Ordinal);
        foreach(var r in resultados)
        {
            if(!_ventasPorId.TryGetValue(r.Id,out var fila)){ fila=new FilaVentaCambio(r); _ventasPorId[r.Id]=fila; }
            else fila.Actualizar(r);
            Ventas.Add(fila);
            vistos.Add(r.Id);
        }
        if(_ventasPorId.Count>vistos.Count)
            foreach(var id in _ventasPorId.Keys.Where(k=>!vistos.Contains(k)).ToArray()) _ventasPorId.Remove(id);
        if(_venta is null && Ventas.Count>0) Venta=Ventas[0];
    }
    private void SeleccionarArticulo(SugerenciaVariante? s)
    {
        if(s is null) return;
        Error=null;
        _articuloSeleccionado=s;
        CodigoSalida=s.CodigoInterno;
        OnPropertyChanged(nameof(TextoArticuloPresentacion));
        NotificarArticuloSeleccionado();
    }
    private void InvalidarArticuloSeleccionado()
    {
        if(_articuloSeleccionado is null) return;
        _articuloSeleccionado=null;
        if(!string.IsNullOrEmpty(_codigoSalida)) CodigoSalida=string.Empty;
        OnPropertyChanged(nameof(TextoArticuloPresentacion));
        NotificarArticuloSeleccionado();
    }
    private void LimpiarArticuloSeleccionado()
    {
        _articuloSeleccionado=null;
        CodigoSalida=string.Empty;
        OnPropertyChanged(nameof(TextoArticuloPresentacion));
        MotorArticulos.Limpiar();
        MotorArticulos.EstablecerTexto(string.Empty);
        NotificarArticuloSeleccionado();
    }
    private void NotificarArticuloSeleccionado()
    {
        OnPropertyChanged(nameof(ArticuloSeleccionado));
        OnPropertyChanged(nameof(TieneArticuloSeleccionado));
        OnPropertyChanged(nameof(ArticuloSeleccionadoDetalle));
        OnPropertyChanged(nameof(PuedeAgregarArticulo));
        OnPropertyChanged(nameof(TieneBorradorOperacion));
    }
    private async Task CargarAsync()
    {
        if(Venta is null)return;
        var ventaCargada=Venta;
        var secuencia=++_secuenciaDetalle;
        Error=null;
        CargandoDetalle=true;
        try
        {
            var lineas=await _s.ConsultasCambios.ObtenerEntradasDisponiblesAsync(ventaCargada.Resumen.Id,_sesion());
            if(secuencia!=_secuenciaDetalle||!ReferenceEquals(Venta,ventaCargada))return;
            Entradas.Clear();foreach(var item in lineas)Entradas.Add(new(item));
            var historial=await _s.ConsultasCambios.ListarAsync(ventaCargada.Resumen.Id,_sesion());
            if(secuencia!=_secuenciaDetalle||!ReferenceEquals(Venta,ventaCargada))return;
            Historial.Clear();foreach(var item in historial)Historial.Add(item);
            try{var ficha=await _s.ConsultasVentas.ObtenerFichaAsync(ventaCargada.Resumen.Id,_sesion());if(secuencia!=_secuenciaDetalle||!ReferenceEquals(Venta,ventaCargada))return;VentaTieneMp=ficha is not null && ficha.Pagos.Any(p=>p.Medio==MediosPago.MercadoPago);}catch{VentaTieneMp=false;}
            ActualizarTotales();OnPropertyChanged(nameof(ReintegroMpPendienteVisible));OnPropertyChanged(nameof(ReintegroMpPendienteTexto));
        }
        catch(Exception ex){if(secuencia==_secuenciaDetalle)Error=ex.Message;}
        finally{if(secuencia==_secuenciaDetalle)CargandoDetalle=false;}
    }
    private Task AgregarSalidaDesdeSeleccionAsync()
    {
        if(_articuloSeleccionado is null){Error="Elegí un artículo de las sugerencias.";return Task.CompletedTask;}
        var s=_articuloSeleccionado;
        var opcion=new OpcionVarianteCambio(s.VarianteId,s.CodigoInterno,s.NombreProducto,s.Talle,s.Color,s.PrecioMinor??0,Math.Max(0,s.Disponible??0));
        return AgregarVarianteAsync(opcion,limpiarSeleccion:true);
    }
    private async Task AgregarSalidaDirectaAsync(string? codigo)
    {
        if(string.IsNullOrWhiteSpace(codigo))return;
        try
        {
            var opcion=await _s.ConsultasCambios.ObtenerVarianteExactaAsync(codigo,_sesion());
            if(opcion is null){Error="No se encontró una variante activa con precio vigente para ese código.";return;}
            await AgregarVarianteAsync(opcion,limpiarSeleccion:false);
        }
        catch(Exception ex){Error=ex.Message;}
    }
    private Task AgregarVarianteAsync(OpcionVarianteCambio opcion,bool limpiarSeleccion)
    {
        Error=null;
        if(!PuedeEditarOperacion)return Task.CompletedTask;
        if(CantidadSalida<=0){Error="La cantidad debe ser mayor que cero.";return Task.CompletedTask;}
        if(opcion.PrecioMinor<=0){Error="La variante no tiene precio vigente.";return Task.CompletedTask;}
        if(opcion.Disponible<CantidadSalida){Error=$"Solo hay {opcion.Disponible} unidades disponibles.";return Task.CompletedTask;}
        var existente=Salidas.FirstOrDefault(x=>x.Opcion.VarianteId==opcion.VarianteId);
        if(existente is null){existente=new(opcion,CantidadSalida);Salidas.Add(existente);}else existente.Cantidad+=CantidadSalida;
        if(limpiarSeleccion)LimpiarArticuloSeleccionado();else CodigoSalida=string.Empty;
        CantidadSalida=1;ActualizarTotales();
        return Task.CompletedTask;
    }
    private async Task ReembolsarMpAsync()
    {
        if(!PuedeReembolsarMp||Venta is null||!_reintegrosMpPendientes.TryGetValue(Venta.Resumen.Id,out var pendiente))return;
        Error=null;Mensaje=null;Ocupado=true;var ventaId=Venta.Resumen.Id;
        try
        {
            var ficha=await _s.ConsultasVentas.ObtenerFichaAsync(ventaId,_sesion());var pagoMp=ficha?.Pagos.FirstOrDefault(p=>p.Medio==MediosPago.MercadoPago);
            if(pagoMp is null){Error="La venta ya no tiene cobro de Mercado Pago.";return;}
            var reop=await _s.CasoReembolsarMercadoPago.EjecutarAsync(ventaId,pagoMp.Id,pendiente.Importe,LocalInfantil.Application.Features.Autenticacion.PermisosComerciales.ReintegroAutorizar,_sesion());
            try { await _s.CasoReembolsarMercadoPago.MarcarConfirmadoLocalAsync(reop,_sesion()); }
            catch(Exception ex) { MarcarReintegroMpIncierto(ventaId,pendiente.Importe); Mensaje=ReintegroMpPendienteTexto; Error=ex.Message; return; }
            _reintegrosMpPendientes.Remove(ventaId);OnPropertyChanged(nameof(PuedeReembolsarMp));OnPropertyChanged(nameof(ReintegroMpPendienteVisible));OnPropertyChanged(nameof(ReintegroMpPendienteTexto));Mensaje="Reintegro por Mercado Pago confirmado.";
        }
        catch(Exception ex)
        {
            if(ex.Message.Contains("pendiente de verificar",StringComparison.OrdinalIgnoreCase)||ex.Message.Contains("verificando el estado",StringComparison.OrdinalIgnoreCase)||ex.Message.Contains("reembolso en curso",StringComparison.OrdinalIgnoreCase))
            { MarcarReintegroMpIncierto(ventaId,pendiente.Importe);Mensaje=ReintegroMpPendienteTexto; }
            Error=ex.Message;
        }
        finally{Ocupado=false;}
    }
    private void MarcarReintegroMpIncierto(string ventaId,long importe){_reintegrosMpPendientes[ventaId]=new(importe,true);OnPropertyChanged(nameof(PuedeReembolsarMp));OnPropertyChanged(nameof(ReintegroMpPendienteVisible));OnPropertyChanged(nameof(ReintegroMpPendienteTexto));}

    private FilaPagoCambio NuevaFilaPago(){var fila=new FilaPagoCambio();fila.PropertyChanged+=(_,_)=>ActualizarTotales();return fila;}
    private void FilaOperacion_PropertyChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){ActualizarTotales();OnPropertyChanged(nameof(TieneBorradorOperacion));OnPropertyChanged(nameof(ErrorCantidadesTexto));}
    private void AgregarPago(){if(!PuedeEditarOperacion||!DiferenciaPositiva)return;Pagos.Add(NuevaFilaPago());ActualizarTotales();}
    private void QuitarPago(FilaPagoCambio? fila){if(!PuedeEditarOperacion||fila is null||Pagos.Count<=1)return;Pagos.Remove(fila);ActualizarTotales();}
    private void QuitarSalida(FilaSalidaCambio? fila){if(!PuedeEditarOperacion||fila is null)return;Salidas.Remove(fila);ActualizarTotales();}
    private void LimpiarOperacionDeVenta(){Entradas.Clear();Salidas.Clear();Pagos.Clear();Pagos.Add(NuevaFilaPago());Historial.Clear();OnPropertyChanged(nameof(HistorialVacio));MedioReintegro=null;Motivo=string.Empty;UsuarioAutorizante=string.Empty;ContrasenaAutorizanteIngresada=false;CodigoSalida=string.Empty;CantidadSalida=1;VentaTieneMp=false;CambioIdReciente=null;Error=null;Mensaje=null;_operacionPendiente=null;SeccionActiva="operacion";ModoOperacion="cambio";LimpiarArticuloSeleccionado();ActualizarTotales();}
    private void LimpiarCamposOperacionRegistrada(){Entradas.Clear();Salidas.Clear();Pagos.Clear();Pagos.Add(NuevaFilaPago());MedioReintegro=null;Motivo=string.Empty;UsuarioAutorizante=string.Empty;CodigoSalida=string.Empty;CantidadSalida=1;_operacionPendiente=null;LimpiarArticuloSeleccionado();ActualizarTotales();}
    private void ActualizarTotales(){if(DiferenciaNegativa&&MedioReintegro is null)MedioReintegro=MediosPago.Efectivo;OnPropertyChanged(nameof(TotalEntradas));OnPropertyChanged(nameof(CreditoEntradasTexto));OnPropertyChanged(nameof(TotalSalidas));OnPropertyChanged(nameof(TotalSalidasTexto));OnPropertyChanged(nameof(Diferencia));OnPropertyChanged(nameof(DiferenciaPositiva));OnPropertyChanged(nameof(DiferenciaCero));OnPropertyChanged(nameof(DiferenciaNegativa));OnPropertyChanged(nameof(ResultadoDiferenciaTexto));OnPropertyChanged(nameof(MuestraAvisoSinDiferencia));OnPropertyChanged(nameof(PuedeContinuarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(ErrorMotivo));OnPropertyChanged(nameof(ErrorCantidadesTexto));OnPropertyChanged(nameof(ErrorCobro));OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));OnPropertyChanged(nameof(AvisoReembolsoMpSeparadoVisible));OnPropertyChanged(nameof(PuedeAgregarArticulo));}
}
