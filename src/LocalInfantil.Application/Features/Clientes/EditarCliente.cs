using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Clientes;

public sealed class EditarCliente(
    IRepositorioClientes clientes,
    IAutorizador autorizador,
    IFabricaSesionesPersistencia fabrica,
    IRepositorioAuditoria auditoria,
    IGeneradorIds ids,
    IReloj reloj)
{
    public async Task EjecutarAsync(string id, EdicionCliente datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var actor = await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Administrar, cancellationToken);
        var nombre = CrearCliente.ExigirNombre(datos.NombreCompleto);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("El cliente es obligatorio.", nameof(id));
        var fecha = reloj.UtcNow().ToString("o");
        await using var sesion = await fabrica.IniciarAsync(cancellationToken);
        var actual = await clientes.ObtenerPorIdAsync(sesion, id, cancellationToken)
            ?? throw new InvalidOperationException("El cliente no existe.");
        if (!actual.Activo) throw new InvalidOperationException("No se puede editar un cliente inactivo.");
        await clientes.ActualizarAsync(sesion, id, new(nombre, CrearCliente.Limpiar(datos.Telefono), CrearCliente.Limpiar(datos.Observacion)), fecha, cancellationToken);
        await auditoria.RegistrarAsync(sesion, new(ids.NuevoId(), "cliente.editado", ActorAuditoria.Usuario, actor.Id, "cliente", id, null), cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
    }
}
