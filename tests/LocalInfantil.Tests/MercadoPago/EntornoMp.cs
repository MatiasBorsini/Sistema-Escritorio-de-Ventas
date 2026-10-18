using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Cambios;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.MercadoPago;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Infrastructure.MercadoPago;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.MercadoPago;

/// <summary>Credencial en memoria (nunca DPAPI real en flujos).</summary>
public sealed class CredencialMemoria : IMercadoPagoCredencialProvider
{
    private string? _token;
    public CredencialMemoria(string? token = null) => _token = token;
    public Task GuardarAsync(string accessToken, CancellationToken ct = default) { _token = accessToken; return Task.CompletedTask; }
    public Task<string?> ObtenerAsync(CancellationToken ct = default) => Task.FromResult(_token);
    public Task EliminarAsync(CancellationToken ct = default) { _token = null; return Task.CompletedTask; }
    public Task<bool> ExisteAsync(CancellationToken ct = default) => Task.FromResult(_token is not null);
}

/// <summary>
/// Entorno F12: DB temporal migrada, servicios reales, cliente HTTP fake,
/// credencial en memoria y terminal configurada. Sin Internet ni secretos.
/// </summary>
public sealed class EntornoMp : IDisposable
{
    public ServiciosComerciales Servicios { get; }
    public string Sesion { get; }
    public FakeMpHandler Fake { get; }
    public CredencialMemoria Credencial { get; }
    public string TerminalId { get; } = "FUTURO_X1__SN000001";
    public IniciarCobroPoint Iniciar { get; }
    public ConsultarCobroPoint Consultar { get; }
    public CancelarCobroPoint Cancelar { get; }
    public ReembolsarMercadoPago Reembolsar { get; }
    public ReconciliarMercadoPago Reconciliar { get; }
    public DescubrirTerminales Descubrir { get; }
    public ConfigurarMercadoPago Configurar { get; }
    public RegistrarCobroManualPoint RegistrarManual { get; }
    public PrevisualizarImportacionLiquidacionMp Previsualizar { get; }
    public ImportarLiquidacionMp Importar { get; }
    public ConciliarLiquidacionMp Conciliar { get; }
    private readonly string _dir;
    private readonly string _ruta;

    private EntornoMp(string dir, string ruta, ServiciosComerciales servicios, string sesion, FakeMpHandler fake, CredencialMemoria cred,
        IniciarCobroPoint iniciar, ConsultarCobroPoint consultar, CancelarCobroPoint cancelar, ReembolsarMercadoPago reembolsar,
        ReconciliarMercadoPago reconciliar, DescubrirTerminales descubrir, ConfigurarMercadoPago configurar, RegistrarCobroManualPoint registrarManual)
    {
        _dir = dir; _ruta = ruta;
        Servicios = servicios; Sesion = sesion; Fake = fake; Credencial = cred;
        Iniciar = iniciar; Consultar = consultar; Cancelar = cancelar; Reembolsar = reembolsar;
        Reconciliar = reconciliar; Descubrir = descubrir; Configurar = configurar; RegistrarManual = registrarManual;
        Previsualizar = servicios.CasoPrevisualizarImportacionMp;
        Importar = servicios.CasoImportarLiquidacionMp;
        Conciliar = servicios.CasoConciliarLiquidacionMp;
    }

    public string RutaDb => _ruta;

    public static async Task<EntornoMp> CrearAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "mp-f12.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        var servicios = new ServiciosComerciales(ruta);
        var duenaId = await servicios.CasoCrearUsuario.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
        var sesion = await servicios.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        foreach (var permiso in new[]
        {
            PermisosCatalogo.ProductosGestionar, PermisosCatalogo.VariantesGestionar, PermisosCatalogo.PreciosGestionar,
            PermisosInventario.CargaInicialEstablecer, PermisosVentas.VentasConfirmar, PermisosVentas.VentasAnular,
            PermisosCaja.Abrir, PermisosCaja.Cerrar, PermisosCaja.Consultar, PermisosCambios.Registrar,
            PermisosComerciales.ReintegroAutorizar, PermisosComerciales.ReintegroMedioExcepcional,
            PermisosMercadoPago.Configurar,
            PermisosMercadoPago.Conciliar,
        })
        {
            await servicios.CasoConfigurarPermiso.EjecutarAsync(duenaId, permiso, true, sesion.IdSesion);
        }
        await servicios.CasoAbrirCaja.EjecutarAsync(0, $"op-apertura-mp-{Guid.NewGuid():N}", sesion.IdSesion);

        var fake = new FakeMpHandler();
        var http = new HttpClient(fake);
        var cliente = new MercadoPagoPointClient(http);
        var cred = new CredencialMemoria("TEST-TOKEN-FAKE");
        // Misma configuración que ven los servicios (Secrets/mercadopago.json):
        // así el POS resuelve la modalidad desde el store compartido.
        var config = servicios.ConfigMercadoPagoStore;
        config.Guardar(new ConfigMercadoPago("FUTURO_X1__SN000001", "PDV", "S1", "P1", null, null) { ModoLocalInfantil = ModosPoint.Integrado });

        var descubrir = new DescubrirTerminales(cred, cliente, servicios.Autorizador);
        var configurar = new ConfigurarMercadoPago(cred, config, servicios.RepoMercadoPago, cliente, servicios.Autorizador, servicios.RepoAuditoria, servicios.Fabrica, servicios.GeneradorIds, servicios.Reloj);
        var registrarManual = new RegistrarCobroManualPoint(servicios.RepoMercadoPago, config, servicios.CasoConfirmarVenta, servicios.RepoCaja, servicios.Autorizador, servicios.Fabrica, servicios.Reloj);
        var iniciar = new IniciarCobroPoint(servicios.RepoMercadoPago, servicios.RepoVariantes, servicios.RepoProductos, servicios.RepoPrecios, servicios.RepoMovimientos, servicios.RepoApartados, servicios.RepoCaja, cred, config, cliente, servicios.Autorizador, servicios.Fabrica, servicios.GeneradorIds, servicios.Reloj);
        var consultar = new ConsultarCobroPoint(servicios.RepoMercadoPago, servicios.RepoVentas, servicios.CasoConfirmarVenta, cred, cliente, servicios.Fabrica, servicios.Reloj);
        var cancelar = new CancelarCobroPoint(servicios.RepoMercadoPago, cred, cliente, servicios.Fabrica, servicios.Reloj);
        var reembolsar = new ReembolsarMercadoPago(servicios.RepoMercadoPago, servicios.RepoVentas, servicios.RepoPagos, cred, cliente, servicios.Autorizador, servicios.Fabrica, servicios.GeneradorIds, servicios.Reloj);
        var reconciliar = new ReconciliarMercadoPago(servicios.RepoMercadoPago, consultar, cred, cliente, servicios.Fabrica, servicios.Reloj, reembolsar);

        return new EntornoMp(dir, ruta, servicios, sesion.IdSesion, fake, cred, iniciar, consultar, cancelar, reembolsar, reconciliar, descubrir, configurar, registrarManual);
    }

    /// <summary>Cambia la modalidad local persistida (integrado/manual/desactivado).</summary>
    public void EstablecerModo(string modo)
    {
        var actual = Servicios.ConfigMercadoPagoStore.Leer();
        Servicios.ConfigMercadoPagoStore.Guardar(actual with { ModoLocalInfantil = modo });
    }

    /// <summary>Producto con 1 variante (10 u., precio 10000).</summary>
    public async Task<(string Prod, string V1, long Precio)> CatalogoConStockAsync()
    {
        var prod = await Servicios.CasoCrearProducto.EjecutarAsync(new NuevoProducto("Remera MP", null, "cat-rem"), Sesion);
        var v1 = await Servicios.CasoCrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", null), Sesion);
        await Servicios.CasoFijarPrecio.EjecutarAsync(v1, 10000, Sesion);
        await Servicios.CasoCargarExistenciaInicial.EjecutarAsync(new[] { new FilaCargaInicial(v1, 10) }, Guid.NewGuid().ToString("N"), Sesion);
        return (prod, v1, 10000);
    }

    public void ResponderTerminales(string modo = "PDV", int total = 1)
    {
        Fake.Responder("GET", "/terminals/v1/list", (req, _) =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri?.Query ?? string.Empty);
            var offset = int.TryParse(query["offset"], out var o) ? o : 0;
            var limit = int.TryParse(query["limit"], out var l) ? l : 50;
            var items = Enumerable.Range(0, total).Skip(offset).Take(limit)
                .Select(i => $"{{\"id\":\"{(i == 0 ? TerminalId : $"OTRA_T{i}__SN{i}")}\",\"pos_id\":\"P1\",\"store_id\":\"S1\",\"external_pos_id\":\"SUC01\",\"operating_mode\":\"{modo}\"}}");
            return FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
                $"{{\"data\":{{\"terminals\":[{string.Join(",", items)}]}},\"paging\":{{\"total\":{total},\"offset\":{offset},\"limit\":{limit}}}}}");
        });
    }

    public void ResponderCrearOrden(string orderId = "ORD000001", string estado = "created")
    {
        Fake.Responder("POST", "/v1/orders", (req, cuerpo) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/cancel"))
                return FakeMpHandler.Json(System.Net.HttpStatusCode.OK, $"{{\"id\":\"{orderId}\",\"status\":\"canceled\",\"transactions\":{{\"payments\":[]}}}}");
            if (req.RequestUri.AbsolutePath.EndsWith("/refund"))
                return FakeMpHandler.Json(System.Net.HttpStatusCode.OK, $"{{\"id\":\"{orderId}\",\"status\":\"refunded\",\"transactions\":{{\"payments\":[]}}}}");
            return FakeMpHandler.Json(System.Net.HttpStatusCode.Created,
                $"{{\"id\":\"{orderId}\",\"type\":\"point\",\"status\":\"{estado}\",\"external_reference\":\"x\",\"transactions\":{{\"payments\":[{{\"id\":\"PAY1\",\"amount\":\"100.00\",\"status\":\"{estado}\"}}]}}}}");
        });
    }

    public void ResponderOrden(string orderId, string estado)
    {
        Fake.Responder("GET", "/v1/orders/", (_, _) => FakeMpHandler.Json(System.Net.HttpStatusCode.OK,
            $"{{\"id\":\"{orderId}\",\"type\":\"point\",\"status\":\"{estado}\",\"status_detail\":\"{estado}\",\"external_reference\":\"x\",\"transactions\":{{\"payments\":[{{\"id\":\"PAY1\",\"amount\":\"100.00\",\"status\":\"{estado}\"}}]}}}}"));
    }

    public void Dispose()
    {
        // El polling del POS puede seguir usando la DB unos instantes tras el
        // assert; reintentamos el borrado para no fallar por lock de archivo.
        for (var intento = 0; intento < 30; intento++)
        {
            try
            {
                if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
                return;
            }
            catch (IOException) when (intento < 29)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(200);
            }
        }
    }
}
