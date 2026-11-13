using System.IO;
using System.Net.Http;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Inicio;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Actualizaciones;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Respaldos;
using LocalInfantil.Infrastructure.MercadoPago;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Tiempo;
using LocalInfantil.Infrastructure.Persistence.Respaldos;
using LocalInfantil.Infrastructure.Printing;

namespace LocalInfantil.Wpf.Infra;

public sealed class ServiciosComerciales
{
    public NotificadorCambiosDatos NotificadorCambios { get; } = new();
    public string RutaBaseDatos { get; }
    public CoordinadorRespaldos CoordinadorRespaldos { get; }
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
    public IRepositorioEtiquetas RepoEtiquetas { get; }
    public IRepositorioPrecios RepoPrecios { get; }
    public IRepositorioMovimientos RepoMovimientos { get; }
    public CrearProducto CasoCrearProducto { get; }
    public EditarProducto CasoEditarProducto { get; }
    public DesactivarProducto CasoDesactivarProducto { get; }
    public CrearVariante CasoCrearVariante { get; }
    public CrearVariantesBatch CasoCrearVariantesBatch { get; }
    public ConsultasMatrizVariantes ConsultasMatrizVariantes { get; }
    public ConsultasEtiquetas ConsultasEtiquetas { get; }
    public IGeneradorCodigoBarras GeneradorCodigoBarras { get; }
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
    public IRepositorioReportes RepoReportes { get; }
    public ConsultasReportes ConsultasReportes { get; }
    public IRepositorioMercadoPago RepoMercadoPago { get; }
    public IMercadoPagoCredencialProvider CredencialMercadoPago { get; }
    public IAlmacenConfigMercadoPago ConfigMercadoPagoStore { get; }
    public IMercadoPagoPointClient ClienteMercadoPago { get; }
    public DescubrirTerminales CasoDescubrirTerminales { get; }
    public ConfigurarMercadoPago CasoConfigurarMercadoPago { get; }
    public RegistrarCobroManualPoint CasoRegistrarCobroManualPoint { get; }
    public IniciarCobroPoint CasoIniciarCobroPoint { get; }
    public ConsultarCobroPoint CasoConsultarCobroPoint { get; }
    public CancelarCobroPoint CasoCancelarCobroPoint { get; }
    public ReembolsarMercadoPago CasoReembolsarMercadoPago { get; }
    public ReconciliarMercadoPago CasoReconciliarMercadoPago { get; }
    public IRepositorioLiquidacionesMp RepoLiquidacionesMp { get; }
    public PrevisualizarImportacionLiquidacionMp CasoPrevisualizarImportacionMp { get; }
    public ImportarLiquidacionMp CasoImportarLiquidacionMp { get; }
    public ConciliarLiquidacionMp CasoConciliarLiquidacionMp { get; }
    public ConsultarConciliacionMp ConsultaConciliacionMp { get; }
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
        RepoEtiquetas = new RepositorioEtiquetas();
        GeneradorCodigoBarras = new GeneradorCodigoBarrasCode128();
        RepoPrecios = new RepositorioPrecios(GeneradorIds);
        RepoMovimientos = new RepositorioMovimientos(GeneradorIds);
        RepoProveedores = new RepositorioProveedores(GeneradorIds);
        RepoRecepciones = new RepositorioRecepciones(GeneradorIds);
        RepoConteos = new RepositorioConteos(GeneradorIds);
        RepoApartados = new RepositorioApartados(GeneradorIds);
        RepoReportes = new RepositorioReportes();
        RepoInicio = new RepositorioInicio(RepoReportes);
        Autorizador = new Autorizador(Gestor, RepoUsuarios, RepoPermisos, Fabrica, Matriz, Hasher);
        var managerBackups = new GestorRespaldosLocales(RutaBaseDatos);
        CoordinadorRespaldos = new CoordinadorRespaldos(managerBackups, new ClienteGoogleDriveRespaldos(managerBackups.DirectorioSecretos), Autorizador);
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
        CasoCrearVariantesBatch = new CrearVariantesBatch(RepoVariantes, RepoProductos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasMatrizVariantes = new ConsultasMatrizVariantes(RepoProductos, RepoVariantes, Autorizador, Fabrica);
        ConsultasEtiquetas = new ConsultasEtiquetas(RepoEtiquetas, Gestor, Fabrica);
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
        ConsultasInventario = new ConsultasInventario(RepoMovimientos, RepoVariantes, RepoConteos, RepoApartados, RepoProductos, Gestor, Fabrica);
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
        RepoMercadoPago = new RepositorioMercadoPago(GeneradorIds);
        CasoCrearCliente = new CrearCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarCliente = new EditarCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoDesactivarCliente = new DesactivarCliente(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasClientes = new ConsultasClientes(Autorizador, Fabrica, RepoClientes);
        ConsultasCuentaCorriente = new ConsultasCuentaCorriente(Autorizador, Fabrica, RepoCuentaCorriente, RepoClientes);
        CasoConfigurarLimiteCredito = new ConfigurarLimiteCredito(RepoClientes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoRegistrarAbono = new RegistrarAbono(RepoCuentaCorriente, RepoClientes, RepoCaja, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj);
        CasoAjustarCuentaCorriente = new AjustarCuentaCorriente(RepoCuentaCorriente, RepoClientes, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj);
        CasoGenerarTicketAbono = new GenerarTicketAbono(Autorizador, Fabrica, RepoCuentaCorriente);
        CasoAbrirCaja = new AbrirCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoRegistrarMovimientoCaja = new RegistrarMovimientoCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoCerrarCaja = new CerrarCaja(RepoCaja, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj, RepoMercadoPago);
        ConsultasCaja = new ConsultasCaja(RepoCaja, Autorizador, Fabrica);
        CasoCorregirMedioPago = new CorregirMedioPago(RepoCaja, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoConfirmarVenta = new ConfirmarVenta(RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, RepoPagos, Autorizador, Fabrica, RepoAuditoria, RepoCaja, GeneradorIds, Reloj, RepoClientes, RepoCuentaCorriente, AutorizadorEnSesion);
        CasoAnularVenta = new AnularVenta(RepoVentas, RepoMovimientos, RepoCaja, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj, RepoCambios, RepoCuentaCorriente);
        ConsultasVentas = new ConsultasVentas(RepoVentas, RepoPagos, Gestor, Fabrica);
        CasoGenerarTicket = new GenerarTicketVenta(ConsultasVentas);
        CasoRegistrarCambio = new RegistrarCambio(RepoCambios, RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, RepoCaja, Autorizador, AutorizadorEnSesion, Fabrica, RepoAuditoria, Reloj, RepoCuentaCorriente, RepoPagos, RepoMercadoPago);
        ConsultasCambios = new ConsultasCambios(Autorizador, Fabrica, RepoCambios, RepoVentas, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, Reloj);
        CasoGenerarTicketCambio = new GenerarTicketCambio(Autorizador, Fabrica, RepoCambios);
        ConsultasReportes = new ConsultasReportes(RepoReportes, RepoCaja, Autorizador, Fabrica);
        var dirSecretosMp = DirSecretosMercadoPago(RutaBaseDatos);
        CredencialMercadoPago = new CredencialPropiaDpapi(dirSecretosMp);
        ConfigMercadoPagoStore = new AlmacenConfigMercadoPago(dirSecretosMp);
        var httpMp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        ClienteMercadoPago = new MercadoPagoPointClient(httpMp);
        CasoDescubrirTerminales = new DescubrirTerminales(CredencialMercadoPago, ClienteMercadoPago, Autorizador);
        CasoConfigurarMercadoPago = new ConfigurarMercadoPago(CredencialMercadoPago, ConfigMercadoPagoStore, RepoMercadoPago, ClienteMercadoPago, Autorizador, RepoAuditoria, Fabrica, GeneradorIds, Reloj);
        CasoRegistrarCobroManualPoint = new RegistrarCobroManualPoint(RepoMercadoPago, ConfigMercadoPagoStore, CasoConfirmarVenta, RepoCaja, Autorizador, Fabrica, Reloj);
        CasoIniciarCobroPoint = new IniciarCobroPoint(RepoMercadoPago, RepoVariantes, RepoProductos, RepoPrecios, RepoMovimientos, RepoApartados, RepoCaja, CredencialMercadoPago, ConfigMercadoPagoStore, ClienteMercadoPago, Autorizador, Fabrica, GeneradorIds, Reloj);
        CasoConsultarCobroPoint = new ConsultarCobroPoint(RepoMercadoPago, RepoVentas, CasoConfirmarVenta, CredencialMercadoPago, ClienteMercadoPago, Fabrica, Reloj);
        CasoCancelarCobroPoint = new CancelarCobroPoint(RepoMercadoPago, CredencialMercadoPago, ClienteMercadoPago, Fabrica, Reloj);
        CasoReembolsarMercadoPago = new ReembolsarMercadoPago(RepoMercadoPago, RepoVentas, RepoPagos, CredencialMercadoPago, ClienteMercadoPago, Autorizador, Fabrica, GeneradorIds, Reloj);
        CasoReconciliarMercadoPago = new ReconciliarMercadoPago(RepoMercadoPago, CasoConsultarCobroPoint, CredencialMercadoPago, ClienteMercadoPago, Fabrica, Reloj, CasoReembolsarMercadoPago);
        RepoLiquidacionesMp = new RepositorioLiquidacionesMp(GeneradorIds);
        CasoConciliarLiquidacionMp = new ConciliarLiquidacionMp(RepoLiquidacionesMp, RepoMercadoPago, RepoVentas, RepoPagos, Autorizador, Fabrica, Reloj);
        CasoPrevisualizarImportacionMp = new PrevisualizarImportacionLiquidacionMp(RepoLiquidacionesMp, Autorizador, Fabrica);
        CasoImportarLiquidacionMp = new ImportarLiquidacionMp(RepoLiquidacionesMp, CasoConciliarLiquidacionMp, Autorizador, Fabrica, Reloj);
        ConsultaConciliacionMp = new ConsultarConciliacionMp(RepoLiquidacionesMp, Autorizador, Fabrica);
    }

    /// <summary>
    /// Secretos MP junto a la DB en uso: en producción van a Secrets/
    /// productivos; con --dev-db van junto a la DB temporal y NUNCA tocan
    /// la credencial productiva.
    /// </summary>
    private static string DirSecretosMercadoPago(string rutaBaseDatos)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(rutaBaseDatos))!;
        if (dir.Equals(LocalInfantil.Infrastructure.Persistence.Sqlite.RutaBaseDatosProductiva.ObtenerDirDatos(), StringComparison.OrdinalIgnoreCase))
            return LocalInfantil.Infrastructure.Persistence.Sqlite.RutaBaseDatosProductiva.ObtenerDirSecretos();
        return Path.Combine(dir, "Secrets");
    }

    /// <summary>
    /// Modalidad efectiva de Point según la configuración no secreta.
    /// </summary>
    public string ObtenerModoPoint()
    {
        var config = ConfigMercadoPagoStore.Leer();
        return ModosPoint.Resolver(config.ModoLocalInfantil, config.TerminalId is not null);
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
        .. PermisosRespaldos.Todos,
        .. PermisosReportes.Todos,
        .. PermisosMercadoPago.Todos,
    ];
}
