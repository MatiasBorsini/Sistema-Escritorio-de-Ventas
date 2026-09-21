using System.Reflection;

namespace LocalInfantil.Tests;

public sealed class SolutionSmokeTests
{
    [Fact]
    public void El_proyecto_de_dominio_esta_disponible_para_las_pruebas()
    {
        var domainAssembly = Assembly.Load("LocalInfantil.Domain");

        Assert.NotNull(domainAssembly);
    }
}
