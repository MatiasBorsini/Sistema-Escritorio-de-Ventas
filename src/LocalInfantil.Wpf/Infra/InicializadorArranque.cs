using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Wpf.Infra;

public sealed record ResultadoInicializacionArranque(bool RequiereBootstrapDuena, IReadOnlyList<int> MigracionesAplicadas);

/// <summary>Prepara la base seleccionada antes de permitir login o bootstrap.</summary>
public sealed class InicializadorArranque(ServiciosComerciales servicios)
{
    public async Task<ResultadoInicializacionArranque> InicializarAsync(string directorioRespaldos, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorioRespaldos);
        var migraciones = await new MigradorBaseDatos(servicios.RutaBaseDatos)
            .MigrarAsync(MigracionesF01.Todas, directorioRespaldos, cancellationToken);
        await using var lectura = await servicios.Fabrica.IniciarLecturaAsync(cancellationToken);
        var cantidadUsuarios = await servicios.RepoUsuarios.ContarTodosAsync(lectura, cancellationToken);
        return new ResultadoInicializacionArranque(cantidadUsuarios == 0, migraciones);
    }
}
