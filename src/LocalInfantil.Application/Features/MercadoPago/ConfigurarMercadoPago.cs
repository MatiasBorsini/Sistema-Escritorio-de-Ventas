using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Configuración MP solo Dueña: credencial (DPAPI, nunca SQLite), terminal
/// seleccionada (config no secreta), activación PDV explícita, prueba de
/// conexión y desconexión. Nunca muestra el token.
/// </summary>
public sealed class ConfigurarMercadoPago
{
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IAlmacenConfigMercadoPago _config;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IAutorizador _autorizador;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public ConfigurarMercadoPago(
        IMercadoPagoCredencialProvider credenciales,
        IAlmacenConfigMercadoPago config,
        IMercadoPagoPointClient cliente,
        IAutorizador autorizador,
        IRepositorioAuditoria auditoria,
        IFabricaSesionesPersistencia fabrica,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _credenciales = credenciales;
        _config = config;
        _cliente = cliente;
        _autorizador = autorizador;
        _auditoria = auditoria;
        _fabrica = fabrica;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<ConfigMercadoPago> ObtenerEstadoAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Configurar, cancellationToken);
        var config = _config.Leer();
        if (!await _credenciales.ExisteAsync(cancellationToken))
            return new ConfigMercadoPago(null, null, null, null, null, config.UltimaComprobacionUtc);
        return config;
    }

    public async Task ConectarAsync(string accessToken, string? idSesion, CancellationToken cancellationToken = default)
    {
        var actor = await AutorizarAsync(idSesion, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Trim().Length < 8)
            throw new ArgumentException("La credencial no es válida.", nameof(accessToken));
        await _credenciales.GuardarAsync(accessToken.Trim(), cancellationToken);
        await AuditarAsync(actor, "mercadopago.conectar", "Credencial configurada.", cancellationToken);
    }

    public async Task SeleccionarTerminalAsync(string terminalId, string? idSesion, CancellationToken cancellationToken = default)
    {
        var actor = await AutorizarAsync(idSesion, cancellationToken);
        if (string.IsNullOrWhiteSpace(terminalId))
            throw new ArgumentException("Seleccioná una terminal disponible.", nameof(terminalId));
        var actual = _config.Leer();
        _config.Guardar(actual with { TerminalId = terminalId.Trim() });
        await AuditarAsync(actor, "mercadopago.seleccionar_terminal", "Terminal seleccionada.", cancellationToken);
    }

    /// <summary>
    /// Activa modo PDV vía PATCH /terminals/v1/setup. Solo acción explícita;
    /// si ya está PDV no hace nada. terminal_not_allowed_action → mensaje humano.
    /// </summary>
    public async Task<TerminalPoint> ActivarModoIntegradoAsync(string terminalId, string? idSesion, CancellationToken cancellationToken = default)
    {
        var actor = await AutorizarAsync(idSesion, cancellationToken);
        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");
        TerminalPoint actual;
        try
        {
            actual = await BuscarTerminalAsync(token, terminalId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }

        if (actual.ModoOperativo == "PDV")
            return actual;

        TerminalPoint terminal;
        try
        {
            terminal = await _cliente.ConfigurarModoAsync(token, terminalId, "PDV", cancellationToken);
        }
        catch (ExcepcionMercadoPago ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }

        var guardada = _config.Leer();
        _config.Guardar(guardada with
        {
            TerminalId = terminal.Id,
            ModoTerminal = terminal.ModoOperativo,
            SucursalId = terminal.SucursalId,
            PosId = terminal.PosId,
        });
        await AuditarAsync(actor, "mercadopago.activar_pdv", "Modo integrado activado.", cancellationToken);
        return terminal;
    }

    public async Task ProbarConexionAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        var actor = await AutorizarAsync(idSesion, cancellationToken);
        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");
        try
        {
            await _cliente.ListarTerminalesAsync(token, 1, 0, cancellationToken);
        }
        catch (ExcepcionMercadoPago ex)
        {
            throw new InvalidOperationException(ex.Message);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
        }

        _config.Guardar(_config.Leer() with { UltimaComprobacionUtc = _reloj.UtcNow().ToString("o") });
        await AuditarAsync(actor, "mercadopago.probar", "Conexión verificada.", cancellationToken);
    }

    public async Task DesconectarAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        var actor = await AutorizarAsync(idSesion, cancellationToken);
        await _credenciales.EliminarAsync(cancellationToken);
        _config.Guardar(new ConfigMercadoPago(null, null, null, null, null, null));
        await AuditarAsync(actor, "mercadopago.desconectar", "Credencial eliminada.", cancellationToken);
    }

    private async Task<TerminalPoint> BuscarTerminalAsync(string token, string terminalId, CancellationToken cancellationToken)
    {
        int desplazamiento = 0;
        while (true)
        {
            var pagina = await _cliente.ListarTerminalesAsync(token, 50, desplazamiento, cancellationToken);
            var terminal = pagina.Terminales.FirstOrDefault(t => t.Id == terminalId);
            if (terminal is not null) return terminal;
            desplazamiento += pagina.Terminales.Count;
            if (desplazamiento >= pagina.Total || pagina.Terminales.Count == 0)
                throw new InvalidOperationException("La terminal ya no aparece en Mercado Pago. Buscá las disponibles.");
        }
    }

    private async Task<UsuarioRegistrado> AutorizarAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        return await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Configurar, cancellationToken);
    }

    private async Task AuditarAsync(UsuarioRegistrado actor, string accion, string detalle, CancellationToken cancellationToken)
    {
        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        await _auditoria.RegistrarAsync(sesion,
            new NuevoEventoAuditoria(_ids.NuevoId(), accion, ActorAuditoria.Usuario, actor.Id, "mercadopago", null, detalle),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
