using System.IO;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Inicio;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Tiempo;

namespace LocalInfantil.Wpf.Infra;

public sealed class ServiciosComerciales
{
    public string RutaBaseDatos { get; }
    public FabricaConexionesSqlite Fabrica { get; }
    public GestorSesiones Gestor { get; }
    public MatrizPermisos Matriz { get; }
    public IHashContrasenas Hasher { get; }
    public IGeneradorIds GeneradorIds { get; }
    public IReloj Reloj { get; }
    public IRepositorioUsuarios RepoUsuarios { get; }
    public IRepositorioPermisos RepoPermisos { get; }
    public IRepositorioAuditoria RepoAuditoria { get; }
    public IAutorizador Autorizador { get; }
    public IAutorizadorEnSesion AutorizadorEnSesion { get; }
    public IniciarSesion CasoIniciarSesion { get; }
    public CerrarSesion CasoCerrarSesion { get; }
    public CrearUsuario CasoCrearUsuario { get; }
    public DesactivarUsuario CasoDesactivarUsuario { get; }
    public RestablecerContrasena CasoRestablecerContrasena { get; }
    public ConfigurarPermiso CasoConfigurarPermiso { get; }
    public IRepositorioProductos RepoProductos { get; }
    public IRepositorioCategorias RepoCategorias { get; }
    public IRepositorioVariantes RepoVariantes { get; }
    public IRepositorioPrecios RepoPrecios { get; }
    public IRepositorioMovimientos RepoMovimientos { get; }
    public CrearProducto CasoCrearProducto { get; }
    public EditarProducto CasoEditarProducto { get; }
    public DesactivarProducto CasoDesactivarProducto { get; }
    public CrearVariante CasoCrearVariante { get; }
    public EditarVariante CasoEditarVariante { get; }
    public DesactivarVariante CasoDesactivarVariante { get; }
    public FijarPrecio CasoFijarPrecio { get; }
    public ConsultasCatalogo ConsultasCatalogo { get; }
    public CargarExistenciaInicial CasoCargarExistenciaInicial { get; }
    public IRepositorioProveedores RepoProveedores { get; }
    public IRepositorioRecepciones RepoRecepciones { get; }
    public IRepositorioConteos RepoConteos { get; }
    public IRepositorioApartados RepoApartados { get; }
    public IRepositorioInicio RepoInicio { get; }
    public ConsultasProveedores ConsultasProveedores { get; }
    public ConsultasRecepciones ConsultasRecepciones { get; }
    public ConsultasConteos ConsultasConteos { get; }
    public ConsultasInventario ConsultasInventario { get; }
    public ConsultasInicio ConsultasInicio { get; }
    public CrearProveedor CasoCrearProveedor { get; }
    public EditarProveedor CasoEditarProveedor { get; }
    public DesactivarProveedor CasoDesactivarProveedor { get; }
    public ConfirmarRecepcion CasoConfirmarRecepcion { get; }
    public RevertirRecepcion CasoRevertirRecepcion { get; }
    public CrearConteo CasoCrearConteo { get; }
    public CorregirConteo CasoCorregirConteo { get; }
    public AprobarConteo CasoAprobarConteo { get; }
    public ApartarStock CasoApartarStock { get; }
    public ReincorporarStock CasoReincorporarStock { get; }
    public BajaDefinitivaApartado CasoBajaDefinitiva { get; }
    public IRepositorioVentas RepoVentas { get; }
    public IRepositorioPagos RepoPagos { get; }
    public ConfirmarVenta CasoConfirmarVenta { get; }
    public AnularVenta CasoAnularVenta { get; }
    public ConsultasVentas ConsultasVentas { get; }
    public GenerarTicketVenta CasoGenerarTicket { get; }
    public IRepositorioCaja RepoCaja { get; }
    public AbrirCaja CasoAbrirCaja { get; }
    public RegistrarMovimientoCaja CasoRegistrarMovimientoCaja { get; }
    public CerrarCaja CasoCerrarCaja { get; }
    public ConsultasCaja ConsultasCaja { get; }
    public CorregirMedioPago CasoCorregirMedioPago { get; }
    public IRepositorioCambios RepoCambios { get; }
    public RegistrarCambio CasoRegistrarCambio { get; }
    public ConsultasCambios ConsultasCambios { get; }
    public GenerarTicketCambio CasoGenerarTicketCambio { get; }
    public IRepositorioClientes RepoClientes { get; }
    public CrearCliente CasoCrearCliente { get; }
    public EditarCliente CasoEditarCliente { get; }
    public DesactivarCliente CasoDesactivarCliente { get; }
    public ConsultasClientes ConsultasClientes { get; }
    public IRepositorioCuentaCorriente RepoCuentaCorriente { get; }
    public ConsultasCuentaCorriente ConsultasCuentaCorriente { get; }
    public ConfigurarLimiteCredito CasoConfigurarLimiteCredito { get; }
    public RegistrarAbono CasoRegistrarAbono { get; }
    public AjustarCuentaCorriente CasoAjustarCuentaCorriente { get; }
    public GenerarTicketAbono CasoGenerarTicketAbono { get; }

    public ServiciosComerciales(string rutaBaseDatos)
    {
        RutaBaseDatos = Path.GetFullPath(rutaBaseDatos);
        Fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(RutaBaseDatos));
        Gestor = new GestorSesiones();
        Matriz = MatrizPermisos.ConValoresConfirmados();
        Hasher = new Pbkdf2HashContrasenas();
        GeneradorIds = new GeneradorIds();
        Reloj = new RelojSistema();
        RepoUsuarios = new RepositorioUsuarios(GeneradorIds);
        RepoPermisos = new RepositorioPermisos();
        RepoAuditoria = new RepositorioAuditoria(GeneradorIds, Reloj);
        RepoProductos = new RepositorioProductos(GeneradorIds);
        RepoCategorias = new RepositorioCategorias(GeneradorIds);
        RepoVariantes = new RepositorioVariantes(GeneradorIds);
        RepoPrecios = new RepositorioPrecios(GeneradorIds);
        RepoMovimientos = new RepositorioMovimientos(GeneradorIds);
        RepoProveedores = new RepositorioProveedores(GeneradorIds);
        RepoRecepciones = new RepositorioRecepciones(GeneradorIds);
        RepoConteos = new RepositorioConteos(GeneradorIds);
        RepoApartados = new RepositorioApartados(GeneradorIds);
        RepoInicio = new RepositorioInicio();
        Autorizador = new Autorizador(Gestor, RepoUsuarios, RepoPermisos, Fabrica, Matriz, Hasher);
        AutorizadorEnSesion = (IAutorizadorEnSesion)Autorizador;
        CasoIniciarSesion = new IniciarSesion(RepoUsuarios, Hasher, Gestor, Fabrica, RepoAuditoria, GeneradorIds, Reloj, new OpcionesAutenticacion());
        CasoCerrarSesion = new CerrarSesion(Gestor, Fabrica, RepoAuditoria, GeneradorIds);
        CasoCrearUsuario = new CrearUsuario(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoDesactivarUsuario = new DesactivarUsuario(RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoRestablecerContrasena = new RestablecerContrasena(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoConfigurarPermiso = new ConfigurarPermiso(RepoPermisos, RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoCrearProducto = new CrearProducto(RepoProductos, RepoCategorias, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarProducto = new EditarProducto(RepoProductos, RepoCategorias, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarProducto = new DesactivarProducto(RepoProductos, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoCrearVariante = new CrearVariante(RepoVariantes, RepoProductos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarVariante = new EditarVariante(RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarVariante = new DesactivarVariante(RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoFijarPrecio = new FijarPrecio(RepoPrecios, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasCatalogo = new ConsultasCatalogo(RepoProductos, RepoVariantes, Gestor, Fabrica);
        CasoCargarExistenciaInicial = new CargarExistenciaInicial(RepoMovimientos, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, Reloj);
        ConsultasProveedores = new ConsultasProveedores(RepoProveedores, Gestor, Fabrica);
        ConsultasRecepciones = new ConsultasRecepciones(RepoRecepciones, Gestor, Fabrica);
        ConsultasConteos = new ConsultasConteos(RepoConteos, Gestor, Fabrica);
        CasoCrearProveedor = new CrearProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarProveedor = new EditarProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarProveedor = new DesactivarProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoConfirmarRecepcion = new ConfirmarRecepcion(RepoRecepciones, RepoProveedores, RepoVariantes, RepoMovimientos, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoRevertirRecepcion = new RevertirRecepcion(RepoRecepciones, RepoMovimientos, RepoApartados, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoCrearConteo = new CrearConteo(RepoConteos, RepoVariantes, RepoMovimientos, RepoApartados, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoCorregirConteo = new CorregirConteo(RepoConteos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoAprobarConteo = new AprobarConteo(RepoConteos, RepoMovimientos, RepoApartados, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasInventario = new ConsultasInventario(RepoMovimientos, RepoVariantes, RepoConteos, RepoApartados, Gestor, Fabrica);
        ConsultasInicio = new ConsultasInicio(Autorizador, RepoInicio, Fabrica);
        CasoApartarStock = new ApartarStock(RepoApartados, RepoVariantes, RepoMovimientos, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoReincorporarStock = new ReincorporarStock(RepoApartados, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoBajaDefinitiva = new BajaDefinitivaApartado(RepoApartados, RepoVariantes, RepoMovimientos, Autorizador, Fabrica, RepoAuditoria, Reloj);
        RepoVentas = new RepositorioVentas(GeneradorIds);
        RepoPagos = new RepositorioPagos(GeneradorIds);
        RepoCaja = new RepositorioCaja(GeneradorIds);
        RepoCambios = new RepositorioCambios(GeneradorIds);
        RepoClientes = new RepositorioClientes(GeneradorIds);
        RepoCuentaCorriente = new RepositorioCuentaCorriente(GeneradorIds);
        CasoCrearCliente = new CrearCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarCliente = new EditarCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoDesactivarCliente = new DesactivarCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasClientes = new ConsultasClientes(Autorizador, Fabrica, RepoClientes);
        ConsultasCuentaCorriente = new ConsultasCuentaCorriente(Autorizador, Fabrica, RepoCuentaCorriente, RepoClientes);
        CasoConfigurarLimiteCredito = new ConfigurarLimiteCredito(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoRegistrarAbono = new RegistrarAbono(RepoCuentaCorriente, RepoClientes, RepoCaja, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj);
        CasoAjustarCuentaCorriente = new AjustarCuentaCorriente(RepoCuentaCorriente, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj);
        CasoGenerarTicketAbono = new GenerarTicketAbono(Autorizador, Fabrica, RepoCuentaCorriente);
        CasoAbrirCaja = new AbrirCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoRegistrarMovimientoCaja = new RegistrarMovimientoCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoCerrarCaja = new CerrarCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasCaja = new ConsultasCaja(RepoCaja, Autorizador, Fabrica);
        CasoCorregirMedioPago = new CorregirMedioPago(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoConfirmarVenta = new ConfirmarVenta(RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, RepoPagos, Autorizador, Fabrica, RepoAuditoria, RepoCaja, GeneradorIds, Reloj, RepoClientes, RepoCuentaCorriente, AutorizadorEnSesion);
        CasoAnularVenta = new AnularVenta(RepoVentas, RepoMovimientos, RepoCaja, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj, RepoCambios, RepoCuentaCorriente);
        ConsultasVentas = new ConsultasVentas(RepoVentas, RepoPagos, Gestor, Fabrica);
        CasoGenerarTicket = new GenerarTicketVenta(ConsultasVentas);
        CasoRegistrarCambio = new RegistrarCambio(RepoCambios, RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, RepoCaja, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj, RepoCuentaCorriente);
        ConsultasCambios = new ConsultasCambios(Autorizador, Fabrica, RepoCambios, RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, Reloj);
        CasoGenerarTicketCambio = new GenerarTicketCambio(Autorizador, Fabrica, RepoCambios);
    }

    public static IReadOnlyList<string> PermisosComercialesConfigurables { get; } =
    [
        PermisosComerciales.ReintegroAutorizar,
        PermisosComerciales.ReintegroMedioExcepcional,
        "catalogo.productos.gestionar",
        "catalogo.variantes.gestionar",
        "catalogo.precios.gestionar",
        "compras.proveedores.gestionar",
        "compras.recepcion.confirmar",
        "compras.recepcion.revertir",
        "inventario.carga_inicial",
        "inventario.ajuste_stock",
        "inventario.conteo_registrar",
        "inventario.conteo_aprobar",
        "inventario.no_vendible.gestionar",
        "inventario.baja_definitiva.autorizar",
        PermisosVentas.VentasConfirmar,
        PermisosVentas.VentasAnular,
        .. PermisosClientes.Todos,
        .. PermisosCuentaCorriente.Todos,
        .. PermisosCambios.Todos,
        .. PermisosCaja.Todos,
    ];
}
