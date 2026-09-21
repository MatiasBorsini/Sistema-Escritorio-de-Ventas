using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Ajusta existencias por variante con un movimiento nuevo e inmutable
/// (positivo o negativo, nunca cero) y motivo obligatorio. Todas las
/// validaciones usan datos confirmados dentro de la misma transacción de
/// escritura: existencia resultante no negativa, operacion_id no repetido y
/// variante existente. Movimiento y auditoría se confirman o revierten juntos.
/// Un ajuste erróneo se corrige con otro ajuste de signo opuesto cuyo motivo
/// cite la operación original; jamás se edita ni borra historial.
/// </summary>
public sealed class AjustarInventario
{
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public AjustarInventario(
        IRepositorioMovimientos movimientos,
        IRepositorioVariantes variantes,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IReloj reloj)
    {
        _movimientos = movimientos;
        _variantes = variantes;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(string varianteId, long cantidad, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (cantidad == 0)
        {
            throw new ArgumentException("La cantidad de ajuste no puede ser cero.", nameof(cantidad));
        }

        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.AjusteStock, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _movimientos.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta operación de ajuste ya fue registrada.");
        }

        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var existencias = await _movimientos.SumarPorVarianteAsync(sesion, varianteId, cancellationToken);
        if (existencias + cantidad < 0)
        {
            throw new InvalidOperationException("El ajuste dejaría existencias negativas.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var id = await _movimientos.RegistrarAsync(
            sesion,
            new NuevoMovimiento(varianteId, "ajuste", cantidad, motivo, operacionId, actor.Id),
            fecha,
            cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.ajuste", ActorAuditoria.Usuario, actor.Id, "variante", varianteId, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
