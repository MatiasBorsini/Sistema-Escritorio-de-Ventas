using LocalInfantil.Application.Features.Persistencia;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Tests.Persistencia;

public sealed class ComprobarPersistenciaLocalTests
{
    [Fact]
    public async Task Guarda_y_recupera_el_valor_tecnico_despues_de_reabrir_la_conexion()
    {
        var directorioTemporal = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var rutaBaseDatos = Path.Combine(directorioTemporal, "prueba-persistencia.db");

        try
        {
            var persistencia = new SqlitePersistenciaTecnicaDePrueba(rutaBaseDatos);
            var casoDeUso = new ComprobarPersistenciaLocal(persistencia);

            var resultado = await casoDeUso.EjecutarAsync();
            var valorTrasReapertura = await persistencia.LeerAsync(ComprobarPersistenciaLocal.ClaveTecnica);

            Assert.True(resultado.Correcta, resultado.MensajeError);
            Assert.True(resultado.EscrituraCorrecta);
            Assert.True(resultado.LecturaCorrecta);
            Assert.True(resultado.DatoCoincide);
            Assert.Equal(resultado.ValorRecuperado, valorTrasReapertura);
        }
        finally
        {
            if (Directory.Exists(directorioTemporal))
            {
                Directory.Delete(directorioTemporal, recursive: true);
            }
        }
    }
}
