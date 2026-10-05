using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Clientes;

public sealed class CrearCliente(
    IRepositorioClientes clientes,
    IAutorizador autorizador,
    IFabricaSesionesPersistencia fabrica,
    IRepositorioAuditoria auditoria,
    IGeneradorIds ids,
    IReloj reloj)
{
    public async Task<string> EjecutarAsync(NuevoCliente datos, string? idSesion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var actor = await autorizador.AutorizarAsync(ExigirSesion(idSesion), PermisosClientes.Administrar, cancellationToken);
        var nombre = ExigirNombre(datos.NombreCompleto);
        var fecha = reloj.UtcNow().ToString("o");
        await using var sesion = await fabrica.IniciarAsync(cancellationToken);
        var id = await clientes.CrearAsync(sesion, new(nombre, Limpiar(datos.Telefono), Limpiar(datos.Observacion)), fecha, cancellationToken);
        await auditoria.RegistrarAsync(sesion, new(ids.NuevoId(), "cliente.creado", ActorAuditoria.Usuario, actor.Id, "cliente", id, null), cancellationToken);
        await sesion.ConfirmarAsync(cancellationToken);
        return id;
    }

    internal static string ExigirNombre(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("El nombre completo es obligatorio.", nameof(valor)) : valor.Trim();

    internal static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    internal static string ExigirSesion(string? idSesion) => idSesion ?? throw new SesionInvalidaException();
}
