using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioClientes
{
    Task<string> CrearAsync(ISesionPersistencia sesion, NuevoCliente datos, string fecha, CancellationToken cancellationToken = default);
    Task<ClienteRegistrado?> ObtenerPorIdAsync(ISesionPersistencia sesion, string id, CancellationToken cancellationToken = default);
    Task ActualizarAsync(ISesionPersistencia sesion, string id, EdicionCliente datos, string fecha, CancellationToken cancellationToken = default);
    Task CambiarActivoAsync(ISesionPersistencia sesion, string id, bool activo, string fecha, CancellationToken cancellationToken = default);
    Task ConfigurarLimiteCreditoAsync(ISesionPersistencia sesion, string id, long? limiteMinor, string fecha, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClienteRegistrado>> BuscarAsync(ISesionPersistencia sesion, string texto, bool incluirInactivos, int limite, int desplazamiento, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SugerenciaCliente>> BuscarSugerenciasAsync(ISesionPersistencia sesion, string texto, int limite, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MovimientoHistorialCliente>> ListarHistorialAsync(ISesionPersistencia sesion, string clienteId, int limite, int desplazamiento, CancellationToken cancellationToken = default);
}
