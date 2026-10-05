using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.CuentaCorriente;

public sealed class AjustarCuentaCorriente(IRepositorioCuentaCorriente cuenta, IAutorizador auth,
    IAutorizadorEnSesion authTx, IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria,
    IReloj reloj)
{
    public async Task<VistaPreviaAjuste> PrevisualizarAsync(string clienteId, bool aumentar, long importe, string? idSesion, CancellationToken ct = default)
    {
        await auth.AutorizarAsync(ConsultasCuentaCorriente.ExigirSesion(idSesion), PermisosCuentaCorriente.Ajustar, ct);
        if (importe <= 0) throw new ArgumentOutOfRangeException(nameof(importe), "El importe debe ser mayor que cero.");
        await using var read = await fabrica.IniciarLecturaAsync(ct);
        var saldo = await cuenta.ObtenerSaldoAsync(read, clienteId, ct); var firmado = aumentar ? importe : -importe;
        if (saldo + firmado < 0) throw new InvalidOperationException("El ajuste no puede dejar un saldo a favor del cliente.");
        return new(saldo, firmado, saldo + firmado);
    }

    public async Task EjecutarAsync(DatosAjusteCuenta d, CancellationToken ct = default)
    {
        if (d.UsuarioSesionId is null) throw new SesionInvalidaException();
        if (d.ImporteMinor <= 0) throw new ArgumentOutOfRangeException(nameof(d), "El importe debe ser mayor que cero.");
        if (string.IsNullOrWhiteSpace(d.Motivo)) throw new ArgumentException("El motivo es obligatorio.");
        if (string.IsNullOrWhiteSpace(d.OperacionId)) throw new ArgumentException("La operación necesita identificador.");
        var authAntes = await auth.AutorizarAsync(d.UsuarioSesionId, PermisosCuentaCorriente.Ajustar, ct);
        await using var tx = await fabrica.IniciarAsync(ct);
        var actor = await authTx.AutorizarAsync(tx, d.UsuarioSesionId, PermisosCuentaCorriente.Ajustar, ct);
        if (actor.Id != authAntes.Id) throw new SesionInvalidaException();
        var saldo = await cuenta.ObtenerSaldoAsync(tx, d.ClienteId, ct);
        if (saldo != d.SaldoEsperadoMinor) throw new InvalidOperationException("El saldo cambió. Revisá nuevamente el ajuste.");
        var firmado = d.AumentarDeuda ? d.ImporteMinor : -d.ImporteMinor;
        if (saldo + firmado < 0) throw new InvalidOperationException("El ajuste no puede dejar un saldo a favor del cliente.");
        var fecha = reloj.UtcNow().ToString("o");
        var reductor = await cuenta.InsertarMovimientoAsync(tx, new(d.ClienteId, d.AumentarDeuda ? TiposMovimientoCuentaCorriente.AjustePositivo : TiposMovimientoCuentaCorriente.AjusteNegativo, firmado, d.OperacionId, actor.Id, fecha, Motivo: d.Motivo.Trim()), ct);
        if (firmado < 0)
        {
            var left = -firmado;
            foreach (var cargo in await cuenta.ListarCargosPendientesAsync(tx, d.ClienteId, ct))
            {
                if (left == 0) break;
                var n = Math.Min(left, cargo.PendienteMinor);
                await cuenta.InsertarAplicacionAsync(tx, reductor, new(cargo.MovimientoId, n), fecha, ct); left -= n;
            }
            if (left != 0) throw new InvalidOperationException("El ajuste supera las deudas asignables del cliente.");
        }
        await auditoria.RegistrarAsync(tx, new(d.OperacionId, "cuenta_corriente.ajuste", ActorAuditoria.Usuario, actor.Id, "cliente", d.ClienteId, $"tipo={(d.AumentarDeuda ? "ajuste_positivo" : "ajuste_negativo")}; importe_minor={firmado}; saldo_anterior_minor={saldo}; saldo_posterior_minor={saldo+firmado}; motivo={d.Motivo.Trim()}"), ct);
        await tx.ConfirmarAsync(ct);
    }
}
