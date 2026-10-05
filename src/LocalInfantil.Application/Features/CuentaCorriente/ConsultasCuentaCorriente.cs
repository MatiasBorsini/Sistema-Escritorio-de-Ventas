using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.CuentaCorriente;

public sealed class ConsultasCuentaCorriente(IAutorizador auth, IFabricaSesionesPersistencia fabrica,
    IRepositorioCuentaCorriente cuenta, IRepositorioClientes clientes)
{
    public async Task<ResumenCuentaCorriente?> ObtenerAsync(string clienteId, string? idSesion, CancellationToken ct = default)
    {
        await auth.AutorizarAsync(ExigirSesion(idSesion), PermisosCuentaCorriente.Consultar, ct);
        await using var read = await fabrica.IniciarLecturaAsync(ct);
        var cliente = await clientes.ObtenerPorIdAsync(read, clienteId, ct);
        if (cliente is null) return null;
        var saldo = await cuenta.ObtenerSaldoAsync(read, clienteId, ct);
        var cargos = await cuenta.ListarCargosPendientesAsync(read, clienteId, ct);
        var disponible = cliente.LimiteCreditoMinor is null ? 0 : Math.Max(0, cliente.LimiteCreditoMinor.Value - saldo);
        return new(clienteId, saldo, cliente.LimiteCreditoMinor, disponible, cargos);
    }

    public async Task<IReadOnlyList<MovimientoCuentaCorriente>> ListarMovimientosAsync(string clienteId, int limite, int desplazamiento, string? idSesion, CancellationToken ct = default)
    {
        await auth.AutorizarAsync(ExigirSesion(idSesion), PermisosCuentaCorriente.Consultar, ct);
        await using var read = await fabrica.IniciarLecturaAsync(ct);
        return await cuenta.ListarMovimientosAsync(read, clienteId, limite, desplazamiento, ct);
    }

    public async Task<VistaPreviaAbono> PrevisualizarAbonoAsync(string clienteId, long importe, IReadOnlyList<NuevoPago> pagos, string? idSesion, CancellationToken ct = default)
    {
        await auth.AutorizarAsync(ExigirSesion(idSesion), PermisosCuentaCorriente.Abonar, ct);
        ValidarPagos(importe, pagos);
        await using var read = await fabrica.IniciarLecturaAsync(ct);
        var saldo = await cuenta.ObtenerSaldoAsync(read, clienteId, ct);
        if (importe > saldo) throw new InvalidOperationException("El importe supera el saldo pendiente del cliente.");
        var cargos = await cuenta.ListarCargosPendientesAsync(read, clienteId, ct);
        var aplicaciones = PlanificarAplicaciones(cargos, pagos);
        return new(saldo, importe, saldo - importe, cargos, aplicaciones);
    }

    internal static IReadOnlyList<AplicacionCuentaCorriente> PlanificarAplicaciones(IReadOnlyList<CargoCuentaCorriente> cargos, IReadOnlyList<NuevoPago> pagos)
    {
        var pendientes = cargos.Select(c => c.PendienteMinor).ToArray(); var resultado = new List<AplicacionCuentaCorriente>(); var cargoIndex = 0;
        for (var componenteIndex = 0; componenteIndex < pagos.Count; componenteIndex++)
        {
            var pago = pagos[componenteIndex];
            var porComponente = pago.ImporteMinor;
            while (porComponente > 0)
            {
                while (cargoIndex < cargos.Count && pendientes[cargoIndex] == 0) cargoIndex++;
                if (cargoIndex >= cargos.Count) throw new InvalidOperationException("El importe supera el saldo pendiente del cliente.");
                var aplica = Math.Min(porComponente, pendientes[cargoIndex]);
                pendientes[cargoIndex] -= aplica; porComponente -= aplica;
                resultado.Add(new(cargos[cargoIndex].MovimientoId, aplica, null, componenteIndex, pendientes[cargoIndex]));
            }
        }
        return resultado;
    }

    internal static void ValidarPagos(long importe, IReadOnlyList<NuevoPago> pagos)
    {
        if (importe <= 0 || pagos.Count == 0 || pagos.Sum(p => p.ImporteMinor) != importe) throw new ArgumentException("Los medios de pago deben sumar el importe del abono.");
        foreach (var p in pagos)
        {
            if (!MediosPago.Todos.Contains(p.Medio) || p.ImporteMinor <= 0) throw new ArgumentException("Revisá los importes y medios del abono.");
            if (p.Medio == MediosPago.Efectivo && (p.RecibidoMinor is null || p.RecibidoMinor < p.ImporteMinor)) throw new ArgumentException("El efectivo recibido es menor al importe del abono.");
            if (p.Medio != MediosPago.Efectivo && p.RecibidoMinor is not null) throw new ArgumentException("Solo efectivo admite importe recibido.");
        }
    }

    internal static string ExigirSesion(string? idSesion) => idSesion ?? throw new SesionInvalidaException();
}
