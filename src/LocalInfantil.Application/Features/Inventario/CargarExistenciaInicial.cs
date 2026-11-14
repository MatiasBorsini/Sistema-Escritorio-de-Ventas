using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Inventario;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// ESTABLECE la existencia inicial por variante en una sola operación con
/// varias filas. No suma mercadería (eso es recepción de F03) ni corrige
/// existencias: cada variante debe estar sin movimientos previos y cada
/// operacion_id se acepta una sola vez (reintentos seguros sin duplicar).
/// Toda la grilla comparte transacción y auditoría: si una fila falla,
/// no persiste nada.
/// </summary>
public sealed class CargarExistenciaInicial
{
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public CargarExistenciaInicial(
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

    public async Task EjecutarAsync(IReadOnlyList<FilaCargaInicial> filas, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filas);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.CargaInicialEstablecer, cancellationToken);

        if (filas.Count == 0)
        {
            throw new ArgumentException("La grilla no tiene filas.", nameof(filas));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fila in filas)
        {
            if (fila.Cantidad <= 0)
            {
                throw new ArgumentException($"Cantidad inválida para la variante '{fila.VarianteId}': la carga inicial establece existencias positivas.", nameof(filas));
            }

            if (!vistos.Add(fila.VarianteId))
            {
                throw new ArgumentException($"Variante duplicada en la grilla: '{fila.VarianteId}'.", nameof(filas));
            }
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _movimientos.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta carga inicial ya fue registrada.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        foreach (var fila in filas)
        {
            var variante = await _variantes.ObtenerPorIdAsync(sesion, fila.VarianteId, cancellationToken);
            if (variante is null)
            {
                throw new InvalidOperationException($"La variante '{fila.VarianteId}' no existe.");
            }

            if (await _movimientos.ContarPorVarianteAsync(sesion, fila.VarianteId, cancellationToken) > 0)
            {
                throw new InvalidOperationException($"La variante '{variante.CodigoInterno}' ya tiene movimientos; corresponde un ajuste de F03, no una carga inicial.");
            }

            await _movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(fila.VarianteId, "inicial", fila.Cantidad, "Carga inicial", operacionId, actor.Id),
                fecha,
                cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.carga_inicial", ActorAuditoria.Usuario, actor.Id, null, null, $"{filas.Count} variantes"),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
