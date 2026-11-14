namespace LocalInfantil.Infrastructure.Persistence.Respaldos;

public interface IDriveRespaldos : IDisposable
{
    bool Configurado {get;}
    bool Conectado {get;}
    Task ConectarAsync(CancellationToken ct=default);
    Task<string> ReservarIdArchivoAsync(string backupId,CancellationToken ct=default);
    Task<string> SubirAsync(ManifiestoRespaldo manifest,CancellationToken ct=default);
    Task<IReadOnlyList<ClienteGoogleDriveRespaldos.ArchivoDrive>> ListarAsync(CancellationToken ct=default);
    Task DescargarAsync(string fileId,string destino,CancellationToken ct=default);
    Task EliminarAsync(string fileId,CancellationToken ct=default);
}
