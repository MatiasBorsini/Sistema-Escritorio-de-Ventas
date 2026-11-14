using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Clientes;

public sealed class DesactivarCliente(
    IRepositorioClientes clientes,
    IAutorizador autorizador,
    IFabricaSesionesPersistencia fabrica,
    IRepositorioAuditoria auditoria,
    IGeneradorIds ids,
    IReloj reloj)
{
    public Task EjecutarAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
        => CambiarEstadoAsync(id, false, "cliente.desactivado", idSesion, cancellationToken);

    public Task ReactivarAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
        => CambiarEstadoAsync(id, true, "cliente.reactivado", idSesion, cancellationToken);

    private async Task CambiarEstadoAsync(string id, bool activo, string evento, string? idSesion, CancellationToken cancellationToken)
    {
        var actor = await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Administrar, cancellationToken);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El cliente es obligatorio.", nameof(id));
        var fecha = reloj.UtcNow().ToString("o");
        await using var sesion = await fabrica.IniciarAsync(cancellationToken);
        var actual = await clientes.ObtenerPorIdAsync(sesion, id, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");
        if (actual.Activo == activo) throw new InvalidOperationException(activo ? "El cliente ya está activo." : "El cliente ya está inactivo.");
        await clientes.CambiarActivoAsync(sesion, id, activo, fecha, cancellationToken);
        await auditoria.RegistrarAsync(sesion, new(ids.NuevoId(), evento, ActorAuditoria.Usuario, actor.Id, "cliente", id, null), cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
