using System.Collections.ObjectModel;
using System.Windows.Input;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Inicio;
namespace LocalInfantil.Wpf.ViewModels;
public sealed class PortadaViewModel : ViewModelBase
{
 public PortadaViewModel(string nombre, ResumenInicio d, ICommand recibir, ICommand nuevo, ICommand conteo, ICommand inventario) { NombreUsuario=nombre; Inventario=d.Inventario; Actividad=new(d.Actividad); ConteosPendientes=d.ConteosPendientes; IrAConteos=conteo; IrAInventario=inventario; Acciones=new(); if(d.AccionesPermitidas.Contains(ConsultasInicio.RecibirMercaderia))Acciones.Add(new("Recepción de mercadería",recibir)); if(d.AccionesPermitidas.Contains(ConsultasInicio.NuevoProducto))Acciones.Add(new("Nuevo producto",nuevo)); if(d.AccionesPermitidas.Contains(ConsultasInicio.ConteoFisico))Acciones.Add(new("Conteo físico",conteo)); Acciones.Add(new("Consultar inventario",inventario)); }
 public string NombreUsuario{get;} public string Bienvenida=>$"Buen día, {NombreUsuario}."; public string NotaRol=>"Los accesos disponibles respetan los permisos definidos por la dueña."; public ObservableCollection<AccionInicio> Acciones{get;} public int ConteosPendientes{get;} public string TextoConteosPendientes=>ConteosPendientes==1?"1 conteo espera revisión":$"{ConteosPendientes} conteos esperan revisión"; public bool HayConteosPendientes=>ConteosPendientes>0; public string TextoApartado=>Inventario.Apartado==1?"1 unidad está apartada":$"{Inventario.Apartado} unidades están apartadas"; public bool HayApartado=>Inventario.Apartado>0; public bool HayPendientes=>HayConteosPendientes||HayApartado; public ResumenInventarioInicio Inventario{get;} public ObservableCollection<ActividadInicio> Actividad{get;} public bool HayActividad=>Actividad.Count>0; public ICommand IrAConteos{get;} public ICommand IrAInventario{get;}
}
public sealed record AccionInicio(string Titulo,ICommand Comando);
