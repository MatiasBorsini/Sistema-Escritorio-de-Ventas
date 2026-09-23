using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Reincorpora cantidad apartada a disponible cuando vuelve a estar apta,
/// con motivo obligatorio. No altera el total físico. Todo en una
/// transacción con auditoría; el operacion_id se acepta una sola vez.
/// </summary>
public sealed class ReincorporarStock
{
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioVariantes _variantes;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public ReincorporarStock(
        IRepositorioApartados apartados,
        IRepositorioVariantes variantes,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IReloj reloj)
    {
        _apartados = apartados;
        _variantes = variantes;
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

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.NoVendibleGestionar, cancellationToken);

        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a reincorporar debe ser positiva.", nameof(cantidad));
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
            throw new InvalidOperationException("Esta operación de reincorporación ya fue registrada.");
        }

        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, varianteId, cancellationToken);
        if (apartado < cantidad)
        {
            throw new InvalidOperationException("No hay suficientes unidades apartadas para reincorporar.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var id = await _apartados.RegistrarAsync(sesion, varianteId, "reincorporar", cantidad, motivo, operacionId, actor.Id, fecha, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.reincorporado", ActorAuditoria.Usuario, actor.Id, "variante", varianteId, motivo),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
