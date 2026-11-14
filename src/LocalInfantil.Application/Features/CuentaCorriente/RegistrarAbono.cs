using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.CuentaCorriente;

public sealed class RegistrarAbono(IRepositorioCuentaCorriente cuenta, IRepositorioClientes clientes,
    IRepositorioCaja caja, IAutorizador auth, IAutorizadorEnSesion authTx,
    IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IReloj reloj)
{
    public async Task<string> EjecutarAsync(DatosAbono d, CancellationToken ct = default)
    {
        if (d.UsuarioSesionId is null) throw new SesionInvalidaException();
        if (string.IsNullOrWhiteSpace(d.OperacionId)) throw new ArgumentException("La operación necesita identificador.");
        ConsultasCuentaCorriente.ValidarPagos(d.ImporteMinor, d.Pagos);
        var actorAntes = await auth.AutorizarAsync(d.UsuarioSesionId, PermisosCuentaCorriente.Abonar, ct);
        var suma = d.Pagos.Sum(p => p.ImporteMinor);
        if (suma != d.ImporteMinor) throw new ArgumentException("Los medios de pago deben sumar el importe del abono.");

        await using var tx = await fabrica.IniciarAsync(ct);
        var actor = await authTx.AutorizarAsync(tx, d.UsuarioSesionId, PermisosCuentaCorriente.Abonar, ct);
        if (actor.Id != actorAntes.Id) throw new SesionInvalidaException();
        if (await caja.ExisteOperacionAsync(tx, d.OperacionId, ct)) throw new InvalidOperationException("Esta operación ya fue registrada.");
        var cliente = await clientes.ObtenerPorIdAsync(tx, d.ClienteId, ct) ?? throw new InvalidOperationException("El cliente no existe.");
        var saldo = await cuenta.ObtenerSaldoAsync(tx, d.ClienteId, ct);
        if (d.ImporteMinor > saldo) throw new InvalidOperationException("El importe supera el saldo pendiente del cliente.");
        if (d.SaldoEsperadoMinor != saldo) throw new InvalidOperationException("El saldo cambió. Revisá nuevamente la aplicación prevista del abono.");
        var cargos = await cuenta.ListarCargosPendientesAsync(tx, d.ClienteId, ct);
        var aplicaciones = ConsultasCuentaCorriente.PlanificarAplicaciones(cargos, d.Pagos);
        if (!MismasAplicaciones(aplicaciones, d.AplicacionesEsperadas)) throw new InvalidOperationException("Las deudas cambiaron. Revisá nuevamente la aplicación prevista del abono.");
        var sesionCaja = await caja.ObtenerAbiertaAsync(tx, ct) ?? throw new InvalidOperationException("No hay una caja abierta para registrar el abono.");
        var fecha = reloj.UtcNow().ToString("o");
        var numero = $"A-{await cuenta.ObtenerSiguienteNumeroAbonoAsync(tx, ct):D6}";
        var abonoId = await cuenta.InsertarAbonoAsync(tx, numero, d.ClienteId, d.ImporteMinor, saldo, d.OperacionId, actor.Id, fecha, ct);
        var componentes = new List<string>();
        foreach (var p in d.Pagos) componentes.Add(await cuenta.InsertarComponenteAbonoAsync(tx, abonoId, p, ct));
        var reductor = await cuenta.InsertarMovimientoAsync(tx, new(d.ClienteId, TiposMovimientoCuentaCorriente.Abono, -d.ImporteMinor, d.OperacionId, actor.Id, fecha, AbonoId: abonoId), ct);
        foreach (var a in aplicaciones)
        {
            var ix = a.ComponenteIndice ?? throw new InvalidOperationException("La vista previa no identificó el medio del abono.");
            if (ix < 0 || ix >= componentes.Count) throw new InvalidOperationException("El medio del abono no es válido.");
            await cuenta.InsertarAplicacionAsync(tx, reductor, a with { ComponenteAbonoId = componentes[ix], ComponenteIndice = null }, fecha, ct);
        }
        var cajaOp = await caja.InsertarOperacionAsync(tx, new(d.OperacionId, sesionCaja.Id, TiposOperacionCaja.MovimientoManual, actor.Id, fecha), ct);
        for (var i = 0; i < d.Pagos.Count; i++)
        {
            var pago = d.Pagos[i];
            await caja.InsertarMovimientoAsync(tx, new(sesionCaja.Id, cajaOp, TiposMovimientoCaja.Abono, pago.ImporteMinor, null, null, pago.Medio, null, $"Abono {numero}", actor.Id, null, fecha, AbonoComponenteId: componentes[i]), ct);
        }
        await auditoria.RegistrarAsync(tx, new(d.OperacionId, "cuenta_corriente.abono", ActorAuditoria.Usuario, actor.Id, "abono", abonoId, $"numero={numero}; cliente={d.ClienteId}; importe_minor={d.ImporteMinor}; saldo_anterior_minor={saldo}; saldo_posterior_minor={saldo-d.ImporteMinor}"), ct);
        await tx.ConfirmarAsync(ct);
        return abonoId;
    }

    private static bool MismasAplicaciones(IReadOnlyList<AplicacionCuentaCorriente> actual, IReadOnlyList<AplicacionCuentaCorriente> esperada)
    {
        static string Key(AplicacionCuentaCorriente a) => $"{a.ComponenteIndice}:{a.CargoId}:{a.ImporteMinor}";
        return actual.Select(Key).SequenceEqual(esperada.Select(Key));
    }
}
