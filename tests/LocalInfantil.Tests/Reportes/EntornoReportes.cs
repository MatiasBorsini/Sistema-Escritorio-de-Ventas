using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Reportes;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using Microsoft.Data.Sqlite;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// Fixture de reportes con reloj controlable (eventos fechables en distintos
/// días/meses) y matriz con todos los permisos operativos concedidos a Dueña.
/// Base temporal aislada por test; nunca toca datos reales.
/// </summary>
internal sealed class EntornoReportes : IDisposable
{
    private readonly string _directorio;
    private readonly FabricaConexionesSqlite _fabrica;
    private readonly GeneradorIds _ids;
    private readonly RelojFijo _reloj;
    private readonly GestorSesiones _sesiones;

    public readonly CrearProducto CrearProducto;
    public readonly CrearVariante CrearVariante;
    public readonly FijarPrecio FijarPrecio;
    public readonly CargarExistenciaInicial Carga;
    public readonly CrearCliente CrearCliente;
    public readonly ConfigurarLimiteCredito ConfigurarLimite;
    public readonly RegistrarAbono RegistrarAbono;
    public readonly RegistrarCambio RegistrarCambio;
    public readonly ApartarStock Apartar;
    public readonly CrearProveedor CrearProveedor;
    public readonly ConfirmarRecepcion Recepcion;
    public readonly AbrirCaja AbrirCaja;
    public readonly RegistrarMovimientoCaja MovimientoCaja;
    public readonly CerrarCaja CerrarCaja;
    public readonly CorregirMedioPago CorregirMedio;
    public readonly ConfirmarVenta ConfirmarVenta;
    public readonly AnularVenta AnularVenta;
    public readonly ConsultasReportes Reportes;
    public readonly ConsultasVentas Ventas;
    public readonly ConsultasCuentaCorriente Cuenta;
    public readonly IAutorizador Autorizador;

    private string? _sesionId;
    private string? _duenaId;

    public string Sesion => _sesionId ?? throw new InvalidOperationException("Sin sesión registrada.");

    private EntornoReportes(string directorio, FabricaConexionesSqlite fabrica, DateTimeOffset momento)
    {
        _directorio = directorio;
        _fabrica = fabrica;
        _ids = new GeneradorIds();
        _reloj = new RelojFijo(momento);
        _sesiones = new GestorSesiones();
        var productos = new RepositorioProductos(_ids);
        var variantes = new RepositorioVariantes(_ids);
        var movimientos = new RepositorioMovimientos(_ids);
        var apartados = new RepositorioApartados(_ids);
        var precios = new RepositorioPrecios(_ids);
        var ventas = new RepositorioVentas(_ids);
        var pagos = new RepositorioPagos(_ids);
        var caja = new RepositorioCaja(_ids);
        var cambios = new RepositorioCambios(_ids);
        var clientes = new RepositorioClientes(_ids);
        var cuenta = new RepositorioCuentaCorriente(_ids);
        var proveedores = new RepositorioProveedores(_ids);
        var recepciones = new RepositorioRecepciones(_ids);
        var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), _reloj);
        var matriz = MatrizPermisos.ConValoresConfirmados();
        foreach (var permiso in new[]
        {
            PermisosCatalogo.ProductosGestionar,
            PermisosCatalogo.VariantesGestionar,
            PermisosCatalogo.PreciosGestionar,
            PermisosInventario.CargaInicialEstablecer,
            PermisosInventario.NoVendibleGestionar,
            PermisosCompras.ProveedoresGestionar,
            PermisosCompras.RecepcionConfirmar,
            PermisosVentas.VentasConfirmar,
            PermisosVentas.VentasAnular,
            PermisosCaja.Abrir,
            PermisosCaja.Consultar,
            PermisosCaja.Movimiento,
            PermisosCaja.Cerrar,
            PermisosCaja.RetiroPropietario,
            PermisosCaja.CorregirMedio,
            PermisosCambios.Registrar,
            PermisosCambios.Consultar,
            PermisosClientes.Administrar,
            PermisosCuentaCorriente.VenderFiado,
            PermisosCuentaCorriente.Abonar,
            PermisosCuentaCorriente.Ajustar,
            PermisosCuentaCorriente.ConfigurarLimite,
            PermisosReportes.Ventas,
            PermisosReportes.Caja,
            PermisosReportes.Inventario,
            PermisosReportes.Costos,
        })
        {
            matriz.Permitir(RolesLocal.Duena, permiso);
        }

        var autorizador = new Autorizador(_sesiones, new RepositorioUsuarios(_ids), new RepositorioPermisos(), _fabrica, matriz);
        Autorizador = autorizador;
        var enSesion = (IAutorizadorEnSesion)autorizador;
        CrearProducto = new CrearProducto(productos, new RepositorioCategorias(_ids), autorizador, _fabrica, auditoria, _ids, _reloj);
        CrearVariante = new CrearVariante(variantes, productos, autorizador, _fabrica, auditoria, _ids, _reloj);
        FijarPrecio = new FijarPrecio(precios, variantes, autorizador, _fabrica, auditoria, _ids, _reloj);
        Carga = new CargarExistenciaInicial(movimientos, variantes, autorizador, _fabrica, auditoria, _reloj);
        CrearCliente = new CrearCliente(clientes, autorizador, _fabrica, auditoria, _ids, _reloj);
        ConfigurarLimite = new ConfigurarLimiteCredito(clientes, autorizador, _fabrica, auditoria, _ids, _reloj);
        RegistrarAbono = new RegistrarAbono(cuenta, clientes, caja, autorizador, enSesion, _fabrica, auditoria, _reloj);
        RegistrarCambio = new RegistrarCambio(cambios, ventas, variantes, productos, precios, movimientos, apartados, caja, autorizador, enSesion, _fabrica, auditoria, _reloj, cuenta);
        Apartar = new ApartarStock(apartados, variantes, movimientos, autorizador, _fabrica, auditoria, _reloj);
        CrearProveedor = new CrearProveedor(proveedores, autorizador, _fabrica, auditoria, _ids, _reloj);
        Recepcion = new ConfirmarRecepcion(recepciones, proveedores, variantes, movimientos, autorizador, _fabrica, auditoria, _reloj);
        AbrirCaja = new AbrirCaja(caja, autorizador, _fabrica, auditoria, _reloj);
        MovimientoCaja = new RegistrarMovimientoCaja(caja, autorizador, _fabrica, auditoria, _reloj);
        CerrarCaja = new CerrarCaja(caja, autorizador, _fabrica, auditoria, _ids, _reloj);
        CorregirMedio = new CorregirMedioPago(caja, autorizador, _fabrica, auditoria, _reloj);
        ConfirmarVenta = new ConfirmarVenta(ventas, variantes, productos, precios, movimientos, apartados, pagos, autorizador, _fabrica, auditoria, caja, _ids, _reloj, clientes, cuenta, enSesion);
        AnularVenta = new AnularVenta(ventas, movimientos, caja, autorizador, _fabrica, auditoria, _ids, _reloj, cambios, cuenta);
        Reportes = new ConsultasReportes(new RepositorioReportes(), caja, autorizador, _fabrica);
        Ventas = new ConsultasVentas(ventas, pagos, _sesiones, _fabrica);
        Cuenta = new ConsultasCuentaCorriente(autorizador, _fabrica, cuenta, clientes);
    }

    public static async Task<EntornoReportes> CrearAsync(DateTimeOffset? momento = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "reportes-f09.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        var entorno = new EntornoReportes(dir, new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta)), momento ?? new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await entorno.SesionDuenaAsync();
        return entorno;
    }

    public async Task<string> SesionDuenaAsync()
    {
        if (_sesionId is not null)
        {
            return _sesionId;
        }

        await using var sesion = await _fabrica.IniciarAsync();
        var usuarios = new RepositorioUsuarios(_ids);
        var hash = new Pbkdf2HashContrasenas();
        var id = await usuarios.CrearAsync(sesion, new NuevoUsuario("duena", RolesLocal.Duena), hash.CrearHuella("clave-duena-0000"), Fecha());
        await sesion.ConfirmarAsync();
        var valida = new SesionUsuario("sesion-duena", id, "duena", RolesLocal.Duena, Fecha());
        _sesiones.Registrar(valida);
        _sesionId = valida.IdSesion;
        _duenaId = id;
        return valida.IdSesion;
    }

    public async Task<string> SesionEmpleadaAsync()
    {
        await using var sesion = await _fabrica.IniciarAsync();
        var usuarios = new RepositorioUsuarios(_ids);
        var hash = new Pbkdf2HashContrasenas();
        var nombre = $"empleada-{Guid.NewGuid():N}";
        var id = await usuarios.CrearAsync(sesion, new NuevoUsuario(nombre, RolesLocal.Empleada), hash.CrearHuella("clave-empleada-00"), Fecha());
        await sesion.ConfirmarAsync();
        var valida = new SesionUsuario($"sesion-{nombre}", id, nombre, RolesLocal.Empleada, Fecha());
        _sesiones.Registrar(valida);
        return valida.IdSesion;
    }

    public void Avanzar(TimeSpan paso) => _reloj.Avanzar(paso);

    public void FijarHora(DateTimeOffset momento)
    {
        Avanzar(momento - _reloj.UtcNow());
    }

    public string Fecha() => _reloj.UtcNow().ToString("o");

    /// <summary>Producto REM con variante M (precio 10000) y S (12000), stock 10/7.</summary>
    public async Task<(string Prod, string V1, string V2)> CatalogoConStockAsync()
    {
        var prod = await CrearProducto.EjecutarAsync(new NuevoProducto("Remera F09", null, "cat-rem"), Sesion);
        var v1 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), Sesion);
        var v2 = await CrearVariante.EjecutarAsync(new NuevaVariante(prod, "S", null), Sesion);
        await FijarPrecio.EjecutarAsync(v1, 10000, Sesion);
        await FijarPrecio.EjecutarAsync(v2, 12000, Sesion);
        await Carga.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10), new FilaCargaInicial(v2, 7) }, Guid.NewGuid().ToString("N"), Sesion);
        return (prod, v1, v2);
    }

    public async Task<string> ClienteHabilitadoAsync(long limite)
    {
        var cliente = await CrearCliente.EjecutarAsync(new NuevoCliente("Cliente F09", null, null), Sesion);
        await ConfigurarLimite.EjecutarAsync(cliente, limite, Sesion);
        return cliente;
    }

    public async Task<string> AbrirCajaAsync(long fondo = 0)
    {
        return await AbrirCaja.EjecutarAsync(fondo, Guid.NewGuid().ToString("N"), Sesion);
    }

    public async Task<IReadOnlyList<PagoRegistrado>> PagosDeVentaAsync(string ventaId)
    {
        var pagos = new RepositorioPagos(_ids);
        await using var sesion = await _fabrica.IniciarLecturaAsync();
        return await pagos.ListarPorVentaAsync(sesion, ventaId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directorio)) Directory.Delete(_directorio, true);
    }
}
