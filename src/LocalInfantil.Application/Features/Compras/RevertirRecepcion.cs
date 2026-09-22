using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Compras;

/// <summary>
/// Revierte COMPLETA una recepción confirmada: crea movimientos negativos
/// compensatorios (tipo ajuste, vinculados por operacion_id y motivo a la
/// recepción original) y registra la reversión, sin borrar ni editar cabecera,
/// líneas, costos, movimientos ni auditoría originales. Verifica dentro de la
/// misma transacción que cada variante dispone de stock suficiente: si alguna
/// no lo tiene, rechaza sin modificar nada. Nunca genera stock negativo.
/// Sin reversión parcial, devolución al proveedor ni asientos contables.
/// </summary>
public sealed class RevertirRecepcion
{
    private readonly IRepositorioRecepciones _recepciones;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IAutorizador _autorizador;
    private readonly IFabricaSesionesPersistencia _fabrica;
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public RevertirRecepcion(
        IRepositorioRecepciones recepciones,
        IRepositorioMovimientos movimientos,
        IAutorizador autorizador,
        IFabricaSesionesPersistencia fabrica,
        IRepositorioAuditoria auditoria,
        IReloj reloj)
    {
        _recepciones = recepciones;
        _movimientos = movimientos;
        _autorizador = autorizador;
        _fabrica = fabrica;
        _auditoria = auditoria;
        _reloj = reloj;
    }

    public async Task EjecutarAsync(string recepcionId, string motivo, string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
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

        var actor = await _autorizador.AutorizarAsync(idSesion, PermisosCompras.RecepcionRevertir, cancellationToken);

        await using var sesion = await _fabrica.IniciarAsync(cancellationToken);
        if (await _recepciones.ExisteReversionAsync(sesion, recepcionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta recepción ya fue revertida.");
        }

        if (await _movimientos.ExistePorOperacionAsync(sesion, operacionId, cancellationToken))
        {
            throw new InvalidOperationException("Esta reversión ya fue registrada.");
        }

        var lineas = await _recepciones.ListarLineasAsync(sesion, recepcionId, cancellationToken);
        if (lineas.Count == 0)
        {
            throw new InvalidOperationException("La recepción no existe o no tiene líneas.");
        }

        foreach (var linea in lineas)
        {
            var existencias = await _movimientos.SumarPorVarianteAsync(sesion, linea.VarianteId, cancellationToken);
            if (existencias < linea.Cantidad)
            {
                throw new InvalidOperationException($"Stock insuficiente en la variante '{linea.VarianteId}' para revertir.");
            }
        }

        var fecha = _reloj.UtcNow().ToString("o");
        await _recepciones.InsertarReversionAsync(sesion, recepcionId, operacionId, motivo, actor.Id, fecha, cancellationToken);
        foreach (var linea in lineas)
        {
            await _movimientos.RegistrarAsync(
                sesion,
                new NuevoMovimiento(linea.VarianteId, "ajuste", -linea.Cantidad, $"Reversión de recepción ({motivo})", operacionId, actor.Id),
                fecha,
                cancellationToken);
        }

        await _auditoria.RegistrarAsync(
            sesion,
            new NuevoEventoAuditoria(operacionId, "compra.recepcion_revertida", ActorAuditoria.Usuario, actor.Id, "recepcion", recepcionId, motivo),
            cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
