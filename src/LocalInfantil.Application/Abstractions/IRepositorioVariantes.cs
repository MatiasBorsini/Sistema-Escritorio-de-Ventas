using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IRepositorioVariantes
{
    Task<string> InsertarAsync(ISesionPersistencia sesion, NuevaVariante datos, string creadoEn, CancellationToken cancellationToken = default);

    Task<VarianteRegistrada?> ObtenerPorSkuAsync(ISesionPersistencia sesion, string sku, CancellationToken cancellationToken = default);
}
