using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>
/// Confirma una recepción SUMANDO existencias (movimientos de ingreso, uno por
/// línea) en una sola transacción con auditoría. El operacion_id lo provee el
/// llamador y se acepta una sola vez, incluso tras reaperturas. El costo
/// histórico informado se guarda tal cual (NULL = no informado, nunca cero
/// implícito); sin promedios, FIFO, márgenes ni valoración (D-07 pendiente).
/// </summary>
public sealed class ConfirmarRecepcion
{
    private readonly IRepositorioRecepciones _recepciones;
    private readonly IRepositorioProveedores _proveedores;
    private readonly IRepositorioVariantes _variantes;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public ConfirmarRecepcion(
        IRepositorioRecepciones recepciones,
        IRepositorioProveedores proveedores,
        IRepositorioVariantes variantes,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IReloj reloj)
    {
        _recepciones = recepciones;
        _proveedores = proveedores;
        _variantes = variantes;
        _movimientos = movimientos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _reloj = reloj;
    }

    public async Task<string> EjecutarAsync(
        string proveedorId,
        IReadOnlyList<LineaRecepcion> lineas,
        string operacionId,
        string? comprobanteRef,
        string? idSesion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        if (idSesion is null)
        {
            throw new SesionInvalidaException();
        }

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCompras.RecepcionConfirmar, cancellationToken);

        if (lineas.Count == 0)
        {
            throw new ArgumentException("La recepción no tiene líneas.", nameof(lineas));
        }

        if (string.IsNullOrWhiteSpace(operacionId))
        {
            throw new ArgumentException("La operación necesita identificador.", nameof(operacionId));
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var linea in lineas)
        {
            if (linea.Cantidad <= 0)
            {
                throw new ArgumentException($"Cantidad inválida para la variante '{linea.VarianteId}'.", nameof(lineas));
            }

            if ((linea.CostoUnitarioMinor is null) != (linea.Moneda is null))
            {
                throw new ArgumentException($"Costo y moneda van juntos en la variante '{linea.VarianteId}'.", nameof(lineas));
            }

            if (linea.CostoUnitarioMinor < 0)
            {
                throw new ArgumentException($"Costo inválido en la variante '{linea.VarianteId}'.", nameof(lineas));
            }

            if (!vistos.Add(linea.VarianteId))
            {
                throw new ArgumentException($"Variante duplicada en la recepción: '{linea.VarianteId}'.", nameof(lineas));
            }
        }

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _recepciones.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta recepción ya fue confirmada.");
        }

        var proveedor = await _proveedores.ObtenerPorIdAsync(sesion, proveedorId, cancellationToken);
        if (proveedor is null)
        {
            throw new InvalidOperationException("El proveedor no existe.");
        }

        var fecha = _reloj.UtcNow().ToString("o");
        var id = await _recepciones.InsertarAsync(sesion, proveedorId, operacionId, comprobanteRef, actor.Id, fecha, cancellationToken);
        foreach (var linea in lineas)
        {
            var variante = await _variantes.ObtenerPorIdAsync(sesion, linea.VarianteId, cancellationToken);
            if (variante is null)
            {
                throw new InvalidOperationException($"La variante '{linea.VarianteId}' no existe.");
            }

            await _recepciones.InsertarLineaAsync(sesion, id, linea.VarianteId, linea.Cantidad, linea.CostoUnitarioMinor, linea.Moneda, cancellationToken);
            await _movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(linea.VarianteId, "ingreso", linea.Cantidad, "Recepción", operacionId, actor.Id),
                fecha,
                cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "compra.recepcion_confirmada", ActorAuditoria.Usuario, actor.Id, "recepcion", id, null),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }
}
