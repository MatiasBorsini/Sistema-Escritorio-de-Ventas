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
    public long Cantidad { get => _cantidad; set { if(SetProperty(ref _cantidad, value)) OnPropertyChanged(nameof(ImporteMinor)); } }
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

public sealed class FilaVentaCambio(ResumenVenta resumen)
{
    public ResumenVenta Resumen { get; } = resumen;
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
}

public sealed class CambiosViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _s; private readonly Func<string?> _sesion; private readonly bool _puedeConsultar; private readonly bool _puedeRegistrar;
    private FilaVentaCambio? _venta; private string _filtro=string.Empty; private string? _error; private string? _mensaje; private string _codigoSalida=string.Empty; private long _cantidadSalida=1; private string? _medioReintegro; private string _motivo=string.Empty; private string _usuarioAutorizante=string.Empty; private bool _contrasenaAutorizanteIngresada; private bool _ocupado; private string? _cambioIdReciente; private DateTime? _desde; private DateTime? _hasta; private string? _operacionPendiente; private string _seccionActiva="operacion"; private string _modoOperacion="cambio";
    private sealed record ReintegroPendiente(long Importe,bool VerificacionPendiente=false);
    private readonly Dictionary<string,ReintegroPendiente> _reintegrosMpPendientes=new(StringComparer.Ordinal);
    public CambiosViewModel(ServiciosComerciales s,Func<string?> sesion,bool puedeConsultar,bool puedeRegistrar)
    {
        _s=s;_sesion=sesion;_puedeConsultar=puedeConsultar;_puedeRegistrar=puedeRegistrar;
        BuscarCommand=new AsyncRelayCommand(BuscarAsync); AgregarSalidaCommand=new AsyncRelayCommand(AgregarSalidaAsync); CargarCommand=new AsyncRelayCommand(CargarAsync); AgregarPagoCommand=new RelayCommand(_=>AgregarPago()); QuitarPagoCommand=new RelayCommand(p=>QuitarPago(p as FilaPagoCambio)); QuitarSalidaCommand=new RelayCommand(p=>QuitarSalida(p as FilaSalidaCambio));
        ReembolsarMpCommand=new AsyncRelayCommand(ReembolsarMpAsync);
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
    public Task BuscarAsyncPublic()=>BuscarAsync();
    public string Filtro {get=>_filtro;set=>SetProperty(ref _filtro,value);}
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
            OnPropertyChanged(nameof(PuedeRegistrarOperacion));
            OnPropertyChanged(nameof(PuedeQuitarPago));
            OnPropertyChanged(nameof(PuedeSeleccionarVenta));
            OnPropertyChanged(nameof(VentaContexto));
            OnPropertyChanged(nameof(ResumenLineaTexto));
            OnPropertyChanged(nameof(VentaAnulada));
            OnPropertyChanged(nameof(SinVentaSeleccionada));
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
    public bool Ocupado {get=>_ocupado;set{if(SetProperty(ref _ocupado,value)){OnPropertyChanged(nameof(PuedeEditarOperacion));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(PuedeQuitarPago));OnPropertyChanged(nameof(PuedeSeleccionarVenta));OnPropertyChanged(nameof(PuedeReembolsarMp));}}}
    public DateTime? Desde {get=>_desde;set=>SetProperty(ref _desde,value);}
    public DateTime? Hasta {get=>_hasta;set=>SetProperty(ref _hasta,value);}
    public bool PuedeConsultar=>_puedeConsultar;
    public bool PuedeRegistrar=>_puedeRegistrar;
    public bool PuedeSeleccionarVenta=>PuedeConsultar && !Ocupado;
    public bool PuedeEditarOperacion=>PuedeRegistrar && Venta is { Resumen.Anulada:false } && !Ocupado;
    public bool PuedeQuitarPago=>PuedeEditarOperacion && Pagos.Count>1;
    public bool PuedeRegistrarOperacion=>PuedeEditarOperacion && Entradas.Any(x=>x.Cantidad>0)
        && Entradas.All(x=>x.Cantidad>=0 && x.Cantidad<=x.Item.CantidadDisponible && (x.Cantidad==0 || x.Apta || !string.IsNullOrWhiteSpace(x.Motivo)))
        && Salidas.All(x=>x.Cantidad>0 && x.Cantidad<=x.Opcion.Disponible)
        && (Diferencia>0 ? Pagos.Count>0 && Pagos.All(p=>p.ImporteValido) && Pagos.Sum(p=>p.ImporteMinor)==Diferencia && Pagos.All(p=>!p.EsEfectivo || p.RecibidoMinor>=p.ImporteMinor)
            : Diferencia<0 ? !string.IsNullOrWhiteSpace(Motivo) && (!RequiereAutorizacionPresencial || (!string.IsNullOrWhiteSpace(UsuarioAutorizante) && ContrasenaAutorizanteIngresada)) : true);
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
    public long CantidadSalida {get=>_cantidadSalida;set=>SetProperty(ref _cantidadSalida,value);}
    public string? MedioReintegro {get=>_medioReintegro;set{if(SetProperty(ref _medioReintegro,value)){OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}}
    public string Motivo {get=>_motivo;set{if(SetProperty(ref _motivo,value)){OnPropertyChanged(nameof(ErrorMotivo));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(TieneBorradorOperacion));}}}
    public string UsuarioAutorizante {get=>_usuarioAutorizante;set{if(SetProperty(ref _usuarioAutorizante,value))OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}
    public bool ContrasenaAutorizanteIngresada {get=>_contrasenaAutorizanteIngresada;set{if(SetProperty(ref _contrasenaAutorizanteIngresada,value))OnPropertyChanged(nameof(PuedeRegistrarOperacion));}}
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
    private async Task BuscarAsync()
    {
        Error=null;if(Desde.HasValue&&Hasta.HasValue&&Hasta.Value.Date<Desde.Value.Date){Error="La fecha hasta no puede ser anterior a la fecha desde.";return;}Venta=null;Ventas.Clear();try{Ocupado=true;var result=await _s.ConsultasCambios.BuscarVentasAsync(Filtro,Desde?.ToString("yyyy-MM-dd"),Hasta?.ToString("yyyy-MM-dd"),_sesion());foreach(var item in result)Ventas.Add(new(item));}catch(Exception ex){Error=ex.Message;}finally{Ocupado=false;}
    }
    private async Task CargarAsync()
    {
        if(Venta is null)return;var ventaCargada=Venta;Error=null;try{Ocupado=true;var lineas=await _s.ConsultasCambios.ObtenerEntradasDisponiblesAsync(ventaCargada.Resumen.Id,_sesion());if(!ReferenceEquals(Venta,ventaCargada))return;Entradas.Clear();foreach(var item in lineas)Entradas.Add(new(item));var historial=await _s.ConsultasCambios.ListarAsync(ventaCargada.Resumen.Id,_sesion());if(!ReferenceEquals(Venta,ventaCargada))return;Historial.Clear();foreach(var item in historial)Historial.Add(item);try{var ficha=await _s.ConsultasVentas.ObtenerFichaAsync(ventaCargada.Resumen.Id,_sesion());if(!ReferenceEquals(Venta,ventaCargada))return;VentaTieneMp=ficha is not null && ficha.Pagos.Any(p=>p.Medio==MediosPago.MercadoPago);}catch{VentaTieneMp=false;}ActualizarTotales();OnPropertyChanged(nameof(ReintegroMpPendienteVisible));OnPropertyChanged(nameof(ReintegroMpPendienteTexto));}catch(Exception ex){Error=ex.Message;}finally{Ocupado=false;}
    }
    private async Task AgregarSalidaAsync()
    {
        Error=null;if(!PuedeEditarOperacion)return;try{var opcion=await _s.ConsultasCambios.ObtenerVarianteExactaAsync(CodigoSalida,_sesion());if(opcion is null){Error="No se encontró una variante activa con precio vigente para ese código.";return;}if(CantidadSalida<=0){Error="La cantidad debe ser mayor que cero.";return;}if(opcion.Disponible<CantidadSalida){Error=$"Solo hay {opcion.Disponible} unidades disponibles.";return;}var existente=Salidas.FirstOrDefault(x=>x.Opcion.VarianteId==opcion.VarianteId);if(existente is null){existente=new(opcion,CantidadSalida);Salidas.Add(existente);}else existente.Cantidad+=CantidadSalida;CodigoSalida=string.Empty;CantidadSalida=1;ActualizarTotales();}catch(Exception ex){Error=ex.Message;}
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
    private void FilaOperacion_PropertyChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e){ActualizarTotales();OnPropertyChanged(nameof(TieneBorradorOperacion));}
    private void AgregarPago(){if(!PuedeEditarOperacion||!DiferenciaPositiva)return;Pagos.Add(NuevaFilaPago());ActualizarTotales();}
    private void QuitarPago(FilaPagoCambio? fila){if(!PuedeEditarOperacion||fila is null||Pagos.Count<=1)return;Pagos.Remove(fila);ActualizarTotales();}
    private void QuitarSalida(FilaSalidaCambio? fila){if(!PuedeEditarOperacion||fila is null)return;Salidas.Remove(fila);ActualizarTotales();}
    private void LimpiarOperacionDeVenta(){Entradas.Clear();Salidas.Clear();Pagos.Clear();Pagos.Add(NuevaFilaPago());Historial.Clear();OnPropertyChanged(nameof(HistorialVacio));MedioReintegro=null;Motivo=string.Empty;UsuarioAutorizante=string.Empty;ContrasenaAutorizanteIngresada=false;CodigoSalida=string.Empty;CantidadSalida=1;VentaTieneMp=false;CambioIdReciente=null;Error=null;Mensaje=null;_operacionPendiente=null;SeccionActiva="operacion";ModoOperacion="cambio";ActualizarTotales();}
    private void LimpiarCamposOperacionRegistrada(){Entradas.Clear();Salidas.Clear();Pagos.Clear();Pagos.Add(NuevaFilaPago());MedioReintegro=null;Motivo=string.Empty;UsuarioAutorizante=string.Empty;CodigoSalida=string.Empty;CantidadSalida=1;_operacionPendiente=null;ActualizarTotales();}
    private void ActualizarTotales(){if(DiferenciaNegativa&&MedioReintegro is null)MedioReintegro=MediosPago.Efectivo;OnPropertyChanged(nameof(TotalEntradas));OnPropertyChanged(nameof(CreditoEntradasTexto));OnPropertyChanged(nameof(TotalSalidas));OnPropertyChanged(nameof(TotalSalidasTexto));OnPropertyChanged(nameof(Diferencia));OnPropertyChanged(nameof(DiferenciaPositiva));OnPropertyChanged(nameof(DiferenciaCero));OnPropertyChanged(nameof(DiferenciaNegativa));OnPropertyChanged(nameof(ResultadoDiferenciaTexto));OnPropertyChanged(nameof(MuestraAvisoSinDiferencia));OnPropertyChanged(nameof(PuedeRegistrarOperacion));OnPropertyChanged(nameof(ErrorMotivo));OnPropertyChanged(nameof(ErrorCobro));OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));OnPropertyChanged(nameof(AvisoReembolsoMpSeparadoVisible));}
}
