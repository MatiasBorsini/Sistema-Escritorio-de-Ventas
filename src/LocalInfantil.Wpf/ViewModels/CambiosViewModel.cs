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
    private long _importeMinor;
    private long? _recibidoMinor;
    public string Medio { get=>_medio; set=>SetProperty(ref _medio,value); }
    public long ImporteMinor { get=>_importeMinor; set=>SetProperty(ref _importeMinor,value); }
    public long? RecibidoMinor { get=>_recibidoMinor; set=>SetProperty(ref _recibidoMinor,value); }
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
    private FilaVentaCambio? _venta; private string _filtro=string.Empty; private string? _error; private string? _mensaje; private string _codigoSalida=string.Empty; private long _cantidadSalida=1; private string? _medioReintegro; private string _motivo=string.Empty; private string _usuarioAutorizante=string.Empty; private bool _ocupado; private string? _cambioIdReciente; private DateTime? _desde; private DateTime? _hasta; private string? _operacionPendiente;
    public CambiosViewModel(ServiciosComerciales s,Func<string?> sesion,bool puedeConsultar,bool puedeRegistrar)
    {
        _s=s;_sesion=sesion;_puedeConsultar=puedeConsultar;_puedeRegistrar=puedeRegistrar;
        BuscarCommand=new AsyncRelayCommand(BuscarAsync); AgregarSalidaCommand=new AsyncRelayCommand(AgregarSalidaAsync); CargarCommand=new AsyncRelayCommand(CargarAsync); AgregarPagoCommand=new RelayCommand(_=>Pagos.Add(new FilaPagoCambio()));
        ReembolsarMpCommand=new AsyncRelayCommand(ReembolsarMpAsync);
        Pagos.Add(new FilaPagoCambio());
    }
    public ObservableCollection<FilaVentaCambio> Ventas {get;}=[];
    public ObservableCollection<FilaLineaCambio> Entradas {get;}=[];
    public ObservableCollection<FilaSalidaCambio> Salidas {get;}=[];
    public ObservableCollection<FilaPagoCambio> Pagos {get;}=[];
    public ObservableCollection<ResumenCambio> Historial {get;}=[];
    public string[] Medios {get;}=[MediosPago.Efectivo,MediosPago.Transferencia,MediosPago.Debito,MediosPago.Credito,MediosPago.Billetera];
    public string[] MediosReintegro {get;}=[MediosPago.Efectivo,MediosPago.Transferencia,MediosPago.Debito,MediosPago.Credito,MediosPago.Billetera,MediosPago.MercadoPago];
    public bool VentaTieneMp {get=>_ventaTieneMp;private set=>SetProperty(ref _ventaTieneMp,value);}
    private bool _ventaTieneMp;
    public bool AvisoReintegroMpVisible => Diferencia<0 && VentaTieneMp && MedioReintegro!=MediosPago.MercadoPago;
    public string AvisoReintegroMp => "El pago original de Mercado Pago no será reembolsado automáticamente.";
    public bool PuedeReembolsarMp => _reintegroMpPendiente is not null && !Ocupado;
    private (string VentaId, long Importe)? _reintegroMpPendiente;
    public AsyncRelayCommand ReembolsarMpCommand {get;}
    public string Filtro {get=>_filtro;set=>SetProperty(ref _filtro,value);}
    public FilaVentaCambio? Venta {get=>_venta;set{if(SetProperty(ref _venta,value)&&value is not null)_=CargarAsync();}}
    public string? Error {get=>_error;set=>SetProperty(ref _error,value);}
    public string? Mensaje {get=>_mensaje;set=>SetProperty(ref _mensaje,value);}
    public bool Ocupado {get=>_ocupado;set=>SetProperty(ref _ocupado,value);}
    public DateTime? Desde {get=>_desde;set=>SetProperty(ref _desde,value);}
    public DateTime? Hasta {get=>_hasta;set=>SetProperty(ref _hasta,value);}
    public bool PuedeConsultar=>_puedeConsultar;
    public bool PuedeRegistrar=>_puedeRegistrar;
    public bool RequiereAutorizacionPresencial => Diferencia < 0 && MedioReintegro is not null && MedioReintegro != MediosPago.Efectivo && _s.Gestor.Obtener(_sesion() ?? string.Empty)?.Rol != "duena";
    public System.Windows.Input.ICommand AgregarPagoCommand{get;}
    public string CodigoSalida {get=>_codigoSalida;set=>SetProperty(ref _codigoSalida,value);}
    public long CantidadSalida {get=>_cantidadSalida;set=>SetProperty(ref _cantidadSalida,value);}
    public string? MedioReintegro {get=>_medioReintegro;set{if(SetProperty(ref _medioReintegro,value)){OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));}}}
    public string Motivo {get=>_motivo;set=>SetProperty(ref _motivo,value);}
    public string UsuarioAutorizante {get=>_usuarioAutorizante;set=>SetProperty(ref _usuarioAutorizante,value);}
    public string? CambioIdReciente {get=>_cambioIdReciente;private set=>SetProperty(ref _cambioIdReciente,value);}
    public long TotalEntradas=>Entradas.Sum(x=>x.ImporteMinor);
    public long TotalSalidas=>Salidas.Sum(x=>x.ImporteMinor);
    public long Diferencia=>TotalSalidas-TotalEntradas;
    public string DiferenciaTexto=>$"{(Diferencia>=0?"A cobrar":"A reintegrar")}: {PosViewModel.FormatearArs(Math.Abs(Diferencia))}";
    public AsyncRelayCommand BuscarCommand{get;} public AsyncRelayCommand CargarCommand{get;} public AsyncRelayCommand AgregarSalidaCommand{get;}
    public async Task<DocumentoTicketCambio?> ObtenerTicketAsync(string idCambio)=>await _s.CasoGenerarTicketCambio.EjecutarAsync(idCambio,_sesion());
    public async Task<ResultadoCambio?> ConfirmarAsync(string? contrasenaAutorizante=null, DecisionCuentaCorrienteCambio? decisionCuenta=null, bool confirmarDecisionCuenta=false, long? deudaEsperadaMinor=null)
    {
        Error=null;Mensaje=null; if(Venta is null){Error="Seleccioná una venta.";return null;}
        try
        {
            var entradas=Entradas.Where(x=>x.Cantidad>0).Select(x=>new LineaCambioEntrada(x.Item.Linea.Id,x.Cantidad,x.Apta,x.Apta?null:x.Motivo)).ToArray();
            var salidas=Salidas.Select(x=>new LineaCambioSalida(x.Opcion.VarianteId,x.Cantidad,x.Opcion.PrecioMinor)).ToArray();
            var cobros=Diferencia>0?Pagos.Where(p=>p.ImporteMinor>0).Select(p=>new PagoDiferenciaCambio(p.Medio,p.ImporteMinor,p.Medio==MediosPago.Efectivo?p.RecibidoMinor:null)).ToArray():Array.Empty<PagoDiferenciaCambio>();
            _operacionPendiente ??= _s.GeneradorIds.NuevoId();
            var datos=new DatosCambio(Venta.Resumen.Id,entradas,salidas,cobros,MedioReintegro,string.IsNullOrWhiteSpace(Motivo)?null:Motivo,_operacionPendiente,_sesion(),Diferencia<0&&MedioReintegro is not null&&MedioReintegro!=MediosPago.Efectivo?UsuarioAutorizante:null,contrasenaAutorizante,decisionCuenta,confirmarDecisionCuenta,deudaEsperadaMinor);
            var diferenciaTexto=DiferenciaTexto;Ocupado=true; var result=await _s.CasoRegistrarCambio.EjecutarAsync(datos);
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
            if(Diferencia<0 && MedioReintegro==MediosPago.MercadoPago && Venta is not null)
            {_reintegroMpPendiente=(Venta.Resumen.Id,-Diferencia);Mensaje+=$". Cr�dito reconocido. Reintegro por Mercado Pago pendiente: ejecutalo con Reembolsar.";OnPropertyChanged(nameof(PuedeReembolsarMp));}
            _operacionPendiente = null;
            await CargarAsync();
            return result;
        }
        catch(Exception ex){Error=ex.Message; return null;}
        finally{Ocupado=false;}
    }
    private async Task BuscarAsync()
    {
        Error=null;if(Desde.HasValue&&Hasta.HasValue&&Hasta.Value.Date<Desde.Value.Date){Error="La fecha hasta no puede ser anterior a la fecha desde.";return;}try{Ocupado=true;var result=await _s.ConsultasCambios.BuscarVentasAsync(Filtro,Desde?.ToString("yyyy-MM-dd"),Hasta?.ToString("yyyy-MM-dd"),_sesion());Ventas.Clear();foreach(var item in result)Ventas.Add(new(item));}catch(Exception ex){Error=ex.Message;}finally{Ocupado=false;}
    }
    private async Task CargarAsync()
    {
        if(Venta is null)return;Error=null;try{Ocupado=true;var lineas=await _s.ConsultasCambios.ObtenerEntradasDisponiblesAsync(Venta.Resumen.Id,_sesion());Entradas.Clear();foreach(var item in lineas){var row=new FilaLineaCambio(item);row.PropertyChanged+=(_,_)=>ActualizarTotales();Entradas.Add(row);}var historial=await _s.ConsultasCambios.ListarAsync(Venta.Resumen.Id,_sesion());Historial.Clear();foreach(var item in historial)Historial.Add(item);try{var ficha=await _s.ConsultasVentas.ObtenerFichaAsync(Venta.Resumen.Id,_sesion());VentaTieneMp=ficha is not null && ficha.Pagos.Any(p=>p.Medio==MediosPago.MercadoPago);}catch{VentaTieneMp=false;}ActualizarTotales();}catch(Exception ex){Error=ex.Message;}finally{Ocupado=false;}
    }
    private async Task AgregarSalidaAsync()
    {
        Error=null;try{var opcion=await _s.ConsultasCambios.ObtenerVarianteExactaAsync(CodigoSalida,_sesion());if(opcion is null){Error="No se encontró una variante activa con precio vigente para ese código.";return;}if(CantidadSalida<=0){Error="La cantidad debe ser mayor que cero.";return;}if(opcion.Disponible<CantidadSalida){Error=$"Solo hay {opcion.Disponible} unidades disponibles.";return;}var existente=Salidas.FirstOrDefault(x=>x.Opcion.VarianteId==opcion.VarianteId);if(existente is null){existente=new(opcion,CantidadSalida);existente.PropertyChanged+=(_,_)=>ActualizarTotales();Salidas.Add(existente);}else existente.Cantidad+=CantidadSalida;CodigoSalida=string.Empty;ActualizarTotales();}catch(Exception ex){Error=ex.Message;}
    }
    private async Task ReembolsarMpAsync(){if(Ocupado||_reintegroMpPendiente is null)return;Error=null;Mensaje=null;Ocupado=true;try{var(ventaId,importe)=_reintegroMpPendiente.Value;var ficha=await _s.ConsultasVentas.ObtenerFichaAsync(ventaId,_sesion());var pagoMp=ficha?.Pagos.FirstOrDefault(p=>p.Medio==MediosPago.MercadoPago);if(pagoMp is null){Error="La venta ya no tiene cobro de Mercado Pago.";return;}var reop=await _s.CasoReembolsarMercadoPago.EjecutarAsync(ventaId,pagoMp.Id,importe,LocalInfantil.Application.Features.Autenticacion.PermisosComerciales.ReintegroAutorizar,_sesion());await _s.CasoReembolsarMercadoPago.MarcarConfirmadoLocalAsync(reop,_sesion());_reintegroMpPendiente=null;OnPropertyChanged(nameof(PuedeReembolsarMp));Mensaje="Reintegro por Mercado Pago confirmado.";}catch(Exception ex){Error=ex.Message;}finally{Ocupado=false;}}
    private void ActualizarTotales(){OnPropertyChanged(nameof(TotalEntradas));OnPropertyChanged(nameof(TotalSalidas));OnPropertyChanged(nameof(Diferencia));OnPropertyChanged(nameof(DiferenciaTexto));OnPropertyChanged(nameof(RequiereAutorizacionPresencial));OnPropertyChanged(nameof(AvisoReintegroMpVisible));}
}
