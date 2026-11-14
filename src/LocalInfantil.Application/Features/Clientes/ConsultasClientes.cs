using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Features.Clientes;

public sealed class ConsultasClientes(IAutorizador autorizador, IFabricaSesionesPersistencia fabrica, IRepositorioClientes clientes)
{
    public async Task<ClienteRegistrado?> ObtenerAsync(string clienteId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Consultar, cancellationToken);
        await using var sesion = await fabrica.IniciarLecturaAsync(cancellationToken);
        return await clientes.ObtenerPorIdAsync(sesion, clienteId, cancellationToken);
    }

    public async Task<IReadOnlyList<ClienteRegistrado>> BuscarAsync(string texto, bool incluirInactivos, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Consultar, cancellationToken);
        await using var sesion = await fabrica.IniciarLecturaAsync(cancellationToken);
        return await clientes.BuscarAsync(sesion, texto ?? string.Empty, incluirInactivos, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<SugerenciaCliente>> BuscarSugerenciasAsync(string texto, int limite, string? idSesion, CancellationToken cancellationToken = default)
    {
        await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Consultar, cancellationToken);
        await using var sesion = await fabrica.IniciarLecturaAsync(cancellationToken);
        return await clientes.BuscarSugerenciasAsync(sesion, texto ?? string.Empty, limite, cancellationToken);
    }

    public async Task<IReadOnlyList<MovimientoHistorialCliente>> HistorialAsync(string clienteId, int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await autorizador.AutorizarAsync(CrearCliente.ExigirSesion(idSesion), PermisosClientes.Consultar, cancellationToken);
        await using var sesion = await fabrica.IniciarLecturaAsync(cancellationToken);
        if (await clientes.ObtenerPorIdAsync(sesion, clienteId, cancellationToken) is null) throw new InvalidOperationException("El cliente no existe.");
        return await clientes.ListarHistorialAsync(sesion, clienteId, limite, desplazamiento, cancellationToken);
    }
}
