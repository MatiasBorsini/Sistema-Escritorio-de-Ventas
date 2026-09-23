using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Inventario;

/// <summary>
/// Aparta cantidad disponible como no vendible (dañado/en revisión), con
/// motivo obligatorio. No altera el total físico: físico = disponible +
/// apartado se mantiene. Todo en una transacción con auditoría; el
/// operacion_id se acepta una sola vez.
/// </summary>
public sealed class ApartarStock
{
    private readonly IRepositorioApartados _apartados;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public ApartarStock(
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

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosInventario.NoVendibleGestionar, cancellationToken);

        if (cantidad <= 0)
        {
            throw new ArgumentException("La cantidad a apartar debe ser positiva.", nameof(cantidad));
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
            throw new InvalidOperationException("Esta operación de apartado ya fue registrada.");
        }

        var variante = await _variantes.ObtenerPorIdAsync(sesion, varianteId, cancellationToken);
        if (variante is null)
        {
            throw new InvalidOperationException("La variante no existe.");
        }

        var fisico = await _movimientos.SumarPorVarianteAsync(sesion, varianteId, cancellationToken);
        var apartado = await _apartados.SumarApartadoPorVarianteAsync(sesion, varianteId, cancellationToken);
        if (fisico - apartado < cantidad)
        {
            throw new InvalidOperationException("No hay suficientes unidades disponibles para apartar.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var id = await _apartados.RegistrarAsync(sesion, varianteId, "apartar", cantidad, motivo, operacionId, actor.Id, fecha, cancellationToken);
        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "inventario.apartado", ActorAuditoria.Usuario, actor.Id, "variante", varianteId, motivo),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
