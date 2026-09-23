using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Da de baja definitiva cantidad apartada: deja de existir físicamente.
/// Reduce apartado y físico de manera atómica en la misma transacción: un
/// movimiento de ajuste negativo (historial conservado) más el movimiento de
/// clasificación 'baja_definitiva', más auditoría. Motivo obligatorio y
/// permiso independiente. Ningún historial se borra ni se edita.
/// </summary>
public sealed class BajaDefinitivaApartado
{
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public BajaDefinitivaApartado(
        IRepositorioApartados apartados,
        IRepositorioVariantes variantes,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IReloj reloj)
    {
        _apartados = apartados;
        _variantes = variantes;
        _movimientos = movimientos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(string varianteId, long cantidad, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.BajaDefinitivaAutorizar, cancellationToken);

        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad de baja debe ser positiva.", nameof(cantidad));
        }

        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo es obligatorio.", nameof(motivo));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _apartados.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta baja definitiva ya fue registrada.");
        }

        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, varianteId, cancellationToken);
        if (apartado < cantidad)
        {
            throw new InvalidOperationException("No hay suficientes unidades apartadas para dar de baja.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        await _movimientos.RegistrarAsync(
            sesion,
            new NuevoMovimiento(varianteId, "ajuste", -cantidad, $"Baja definitiva de apartado: {motivo}", operacionId, actor.Id),
            fecha,
            cancellationToken);
        var id = await _apartados.RegistrarAsync(sesion, varianteId, "baja_definitiva", cantidad, motivo, operacionId, actor.Id, fecha, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.baja_definitiva", ActorAuditoria.Usuario, actor.Id, "variante", varianteId, motivo),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
