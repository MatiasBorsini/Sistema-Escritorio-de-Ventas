using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Descubrimiento dinámico de terminales (GET /terminals/v1/list con
/// paginación). Sin lista fija de modelos: la compatibilidad la define
/// operating_mode=PDV devuelto por la API.
/// </summary>
public sealed class DescubrirTerminales
{
    private const int TamanoPagina = 50;
    private readonly IMercadoPagoCredencialProvider _credenciales;
    private readonly IMercadoPagoPointClient _cliente;
    private readonly IAutorizador _autorizador;

    public DescubrirTerminales(
        IMercadoPagoCredencialProvider credenciales,
        IMercadoPagoPointClient cliente,
        IAutorizador autorizador)
    {
        _credenciales = credenciales;
        _cliente = cliente;
        _autorizador = autorizador;
    }

    public async Task<IReadOnlyList<TerminalPoint>> EjecutarAsync(string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null) throw new SesionInvalidaException();
        await _autorizador.AutorizarAsync(idSesion, PermisosMercadoPago.Configurar, cancellationToken);
        var token = await _credenciales.ObtenerAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mercado Pago no está conectado en este equipo.");

        var todas = new List<TerminalPoint>();
        int desplazamiento = 0;
        while (true)
        {
            PaginaTerminales pagina;
            try
            {
                pagina = await _cliente.ListarTerminalesAsync(token, TamanoPagina, desplazamiento, cancellationToken);
            }
            catch (ExcepcionMercadoPago ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
            catch (HttpRequestException)
            {
                throw new InvalidOperationException("Mercado Pago no está disponible. Revisá la conexión.");
            }

            todas.AddRange(pagina.Terminales);
            desplazamiento += pagina.Terminales.Count;
            if (desplazamiento >= pagina.Total || pagina.Terminales.Count == 0)
                break;
        }

        return todas;
    }
}
