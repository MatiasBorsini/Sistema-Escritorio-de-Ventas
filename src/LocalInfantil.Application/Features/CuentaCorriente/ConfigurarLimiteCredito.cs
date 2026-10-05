using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.CuentaCorriente;

public sealed class ConfigurarLimiteCredito(IRepositorioClientes clientes, IAutorizador auth,
    IFabricaSesionesPersistencia fabrica, IRepositorioAuditoria auditoria, IGeneradorIds ids, IReloj reloj)
{
    public async Task EjecutarAsync(string clienteId, long? limiteMinor, string? idSesion, CancellationToken ct = default)
    {
        if (limiteMinor < 0) throw new ArgumentOutOfRangeException(nameof(limiteMinor), "El límite no puede ser negativo.");
        var actor = await auth.AutorizarAsync(ConsultasCuentaCorriente.ExigirSesion(idSesion), PermisosCuentaCorriente.ConfigurarLimite, ct);
        await using var tx = await fabrica.IniciarAsync(ct);
        var actual = await clientes.ObtenerPorIdAsync(tx, clienteId, ct) ?? throw new InvalidOperationException("El cliente no existe.");
        await clientes.ConfigurarLimiteCreditoAsync(tx, clienteId, limiteMinor, reloj.UtcNow().ToString("o"), ct);
        await auditoria.RegistrarAsync(tx, new(ids.NuevoId(), "cliente.limite_credito.configurado", ActorAuditoria.Usuario, actor.Id, "cliente", clienteId, $"limite_minor={(limiteMinor?.ToString() ?? "no_configurado")}; anterior_minor={(actual.LimiteCreditoMinor?.ToString() ?? "no_configurado")}"), ct);
        await tx.ConfirmarAsync(ct);
    }
}
