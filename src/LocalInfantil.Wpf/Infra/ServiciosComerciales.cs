using System.IO;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Autenticacion;
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
        Autorizador = new Autorizador(Gestor, RepoUsuarios, RepoPermisos, Fabrica, Matriz);
        CasoIniciarSesion = new IniciarSesion(RepoUsuarios, Hasher, Gestor, Fabrica, RepoAuditoria, GeneradorIds, Reloj, new OpcionesAutenticacion());
        CasoCerrarSesion = new CerrarSesion(Gestor, Fabrica, RepoAuditoria, GeneradorIds);
        CasoCrearUsuario = new CrearUsuario(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
        CasoDesactivarUsuario = new DesactivarUsuario(RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoRestablecerContrasena = new RestablecerContrasena(RepoUsuarios, Hasher, Autorizador, Fabrica, RepoAuditoria, GeneradorIds);
        CasoConfigurarPermiso = new ConfigurarPermiso(RepoPermisos, RepoUsuarios, Autorizador, Fabrica, RepoAuditoria, GeneradorIds, Reloj);
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
