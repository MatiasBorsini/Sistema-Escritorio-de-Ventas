using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Crea un conteo en borrador con las variantes elegidas y la existencia
/// esperada de cada una (foto al iniciar). Un conteo parcial no supone cero
/// para variantes no incluidas: simplemente no las contiene.
/// </summary>
public sealed class CrearConteo
{
    private readonly IRepositorioConteos _conteos;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IGeneradorIds _ids;
    private readonly IReloj _reloj;

    public CrearConteo(
        IRepositorioConteos conteos,
        IRepositorioVariantes variantes,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IGeneradorIds ids,
        IReloj reloj)
    {
        _conteos = conteos;
        _variantes = variantes;
        _movimientos = movimientos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _ids = ids;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(IReadOnlyList<FilaConteo> filas, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filas);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.ConteoRegistrar, cancellationToken);

        if (filas.Count == 0)
        {
            throw new ArgumentException("El conteo no tiene filas.", nameof(filas));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fila in filas)
        {
            if (fila.CantidadContada < 0)
            {
                throw new ArgumentException($"Cantidad inválida para la variante '{fila.VarianteId}'.", nameof(filas));
            }

            if (!vistos.Add(fila.VarianteId))
            {
                throw new ArgumentException($"Variante duplicada en el conteo: '{fila.VarianteId}'.", nameof(filas));
            }
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _conteos.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Este conteo ya fue registrado.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var id = await _conteos.CrearAsync(sesion, operacionId, actor.Id, fecha, cancellationToken);
        foreach (var fila in filas)
        {
            var variante = await _variantes.ObtenerPorIdAsync(sesion, fila.VarianteId, cancellationToken);
            if (variante is null)
            {
                throw new InvalidOperationException($"La variante '{fila.VarianteId}' no existe.");
            }

            var esperada = await _movimientos.SumarPorVarianteAsync(sesion, fila.VarianteId, cancellationToken);
            var contados = await _movimientos.ContarPorVarianteAsync(sesion, fila.VarianteId, cancellationToken);
            await _conteos.AgregarDetalleAsync(sesion, id, fila.VarianteId, esperada, contados, fila.CantidadContada, cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.conteo_creado", ActorAuditoria.Usuario, actor.Id, null, null, $"{filas.Count} variantes"),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
