using System.IO;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.Inventario;
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
    public IniciarSesion CasoIniciarSesion { get; }
    public CerrarSesion CasoCerrarSesion { get; }
    public CrearUsuario CasoCrearUsuario { get; }
    public DesactivarUsuario CasoDesactivarUsuario { get; }
    public RestablecerContrasena CasoRestablecerContrasena { get; }
    public ConfigurarPermiso CasoConfigurarPermiso { get; }
    public IRepositorioProductos RepoProductos { get; }
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
    public ConsultasProveedores ConsultasProveedores { get; }
    public ConsultasRecepciones ConsultasRecepciones { get; }
    public CrearProveedor CasoCrearProveedor { get; }
    public EditarProveedor CasoEditarProveedor { get; }
    public DesactivarProveedor CasoDesactivarProveedor { get; }
    public ConfirmarRecepcion CasoConfirmarRecepcion { get; }
    public RevertirRecepcion CasoRevertirRecepcion { get; }

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
        RepoVariantes = new RepositorioVariantes(GeneradorIds);
        RepoPrecios = new RepositorioPrecios(GeneradorIds);
        RepoMovimientos = new RepositorioMovimientos(GeneradorIds);
        RepoProveedores = new RepositorioProveedores(GeneradorIds);
        RepoRecepciones = new RepositorioRecepciones(GeneradorIds);
        Autorizador = new Autorizador(Gestor, RepoUsuarios, RepoPermisos, Fabrica, Matriz);
        CasoIniciarSesion = new IniciarSesion(RepoUsuarios, Hasher, Gestor, Fabrica, RepoAuditoria, GeneradorIds, Reloj, new OpcionesAutenticacion());
        CasoCerrarSesion = new CerrarSesion(Gestor, Fabrica, RepoAuditoria, GeneradorIds);
        CasoCrearUsuario = new CrearUsuario(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoDesactivarUsuario = new DesactivarUsuario(RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoRestablecerContrasena = new RestablecerContrasena(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoConfigurarPermiso = new ConfigurarPermiso(RepoPermisos, RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoCrearProducto = new CrearProducto(RepoProductos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarProducto = new EditarProducto(RepoProductos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarProducto = new DesactivarProducto(RepoProductos, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoCrearVariante = new CrearVariante(RepoVariantes, RepoProductos, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarVariante = new EditarVariante(RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarVariante = new DesactivarVariante(RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoFijarPrecio = new FijarPrecio(RepoPrecios, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        ConsultasCatalogo = new ConsultasCatalogo(RepoProductos, RepoVariantes, Gestor, Fabrica);
        CasoCargarExistenciaInicial = new CargarExistenciaInicial(RepoMovimientos, RepoVariantes, Autorizador, Fabrica, RepoAuditoria, Reloj);
        ConsultasProveedores = new ConsultasProveedores(RepoProveedores, Gestor, Fabrica);
        ConsultasRecepciones = new ConsultasRecepciones(RepoRecepciones, Gestor, Fabrica);
        CasoCrearProveedor = new CrearProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoEditarProveedor = new EditarProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoDesactivarProveedor = new DesactivarProveedor(RepoProveedores, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoConfirmarRecepcion = new ConfirmarRecepcion(RepoRecepciones, RepoProveedores, RepoVariantes, RepoMovimientos, Autorizador, Fabrica, RepoAuditoria, Reloj);
        CasoRevertirRecepcion = new RevertirRecepcion(RepoRecepciones, RepoMovimientos, Autorizador, Fabrica, RepoAuditoria, Reloj);
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
    ];
}
