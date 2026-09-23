using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Aprueba un borrador: verifica que ningún movimiento alteró las variantes
/// desde la foto inicial (ni siquiera con saldo neto igual) y que ninguna
/// clasificación cambió desde la foto (ni siquiera con el mismo apartado
/// final), genera un ajuste compensatorio por cada diferencia no nula y marca
/// el conteo como aprobado, todo en una sola transacción con auditoría.
/// Diferencia cero no genera movimiento. La aprobación nunca deja el
/// disponible en negativo. Los conteos anteriores a V8, sin foto de
/// clasificación, se rechazan de forma controlada (nunca aprobación
/// silenciosa). La doble aprobación se rechaza, incluso tras reapertura.
/// </summary>
public sealed class AprobarConteo
{
    private readonly IRepositorioConteos _conteos;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioApartados _apartados;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public AprobarConteo(
        IRepositorioConteos conteos,
        IRepositorioMovimientos movimientos,
        IRepositorioApartados apartados,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _conteos = conteos;
        _movimientos = movimientos;
        _apartados = apartados;
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

            var foto = await _apartados.ObtenerFotoConteoAsync(sesion, conteoId, detalle.VarianteId, cancellationToken);
            if (foto is null)
            {
                throw new InvalidOperationException(
                    $"La variante '{detalle.VarianteId}' no tiene foto de clasificación (conteo anterior a V8): registre un conteo nuevo en lugar de aprobar esta foto incompleta.");
            }

            var apartadoEsperado = foto.ApartadoEsperado;
            var clasificadosFoto = foto.ClasificacionesContadas;
            var clasificadosAhora = await _apartados.ContarPorVarianteAsync(sesion, detalle.VarianteId, cancellationToken);
            if (clasificadosAhora != clasificadosFoto)
            {
                throw new InvalidOperationException(
                    $"La clasificación de la variante '{detalle.VarianteId}' cambió desde el conteo: revise y registre uno nuevo en lugar de aprobar esta foto vieja.");
            }

            var apartadoAhora = await _apartados.SumarApartadoPorVarianteAsync(sesion, detalle.VarianteId, cancellationToken);
            if (apartadoAhora != apartadoEsperado)
            {
                throw new InvalidOperationException(
                    $"La clasificación de la variante '{detalle.VarianteId}' cambió desde el conteo: revise y registre uno nuevo en lugar de aprobar esta foto vieja.");
            }

            var diferencia = detalle.CantidadContada - detalle.ExistenciaEsperada;
            if (diferencia != 0)
            {
                if (detalle.CantidadContada < apartadoAhora)
                {
                    throw new InvalidOperationException(
                        $"Aprobar el conteo dejaría disponible negativo en la variante '{detalle.VarianteId}' (hay unidades apartadas).");
                }

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
