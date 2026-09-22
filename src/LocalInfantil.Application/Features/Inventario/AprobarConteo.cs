using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Aprueba un borrador: verifica que ningún movimiento alteró las variantes
/// desde la foto inicial (ni siquiera con saldo neto igual), genera un ajuste
/// compensatorio por cada diferencia no nula y marca el conteo como aprobado,
/// todo en una sola transacción con auditoría. Diferencia cero no genera
/// movimiento. La doble aprobación se rechaza, incluso tras reapertura.
/// </summary>
public sealed class AprobarConteo
{
    private readonly IRepositorioConteos _conteos;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public AprobarConteo(
        IRepositorioConteos conteos,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _conteos = conteos;
        _movimientos = movimientos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task EjecutarAsync(string conteoId, string operacionAjustesId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operacionAjustesId))
        {
            throw new ArgumentException("La aprobación necesita identificador de operación.", nameof(operacionAjustesId));
        }

        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.ConteoAprobar, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        var conteo = await _conteos.ObtenerAsync(sesion, conteoId, cancellationToken);
        if (conteo is null)
        {
            throw new InvalidOperationException("El conteo no existe.");
        }

        if (conteo.Estado != "borrador")
        {
            throw new InvalidOperationException("El conteo ya fue aprobado.");
        }

        if (await _movimientos.ExistePorOperacionAsync(sesion, operacionAjustesId, cancellationToken))
        {
            throw new InvalidOperationException("Esta aprobación ya fue registrada.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var detalles = await _conteos.ListarDetallesAsync(sesion, conteoId, cancellationToken);
        foreach (var detalle in detalles)
        {
            var contadosAhora = await _movimientos.ContarPorVarianteAsync(sesion, detalle.VarianteId, cancellationToken);
            if (contadosAhora != detalle.MovimientosContados)
            {
                throw new InvalidOperationException(
                    $"La variante '{detalle.VarianteId}' cambió desde el conteo: revise y registre uno nuevo en lugar de aprobar esta foto vieja.");
            }

            var diferencia = detalle.CantidadContada - detalle.ExistenciaEsperada;
            if (diferencia != 0)
            {
                await _movimientos.RegistrarAsync(
                    sesion,
                    new NuevoMovimiento(detalle.VarianteId, "ajuste", diferencia, $"Conteo {conteo.OperacionId}", operacionAjustesId, actor.Id),
                    fecha,
                    cancellationToken);
            }
        }

        await _conteos.AprobarAsync(sesion, conteoId, actor.Id, fecha, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionAjustesId, "inventario.conteo_aprobado", ActorAuditoria.Usuario, actor.Id, null, null, $"{detalles.Count} variantes"),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
