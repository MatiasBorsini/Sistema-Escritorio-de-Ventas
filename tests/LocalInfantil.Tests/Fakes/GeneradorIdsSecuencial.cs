using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Tests.Fakes;

/// <summary>Generador determinista para pruebas: id-0001, id-0002, …</summary>
public sealed class GeneradorIdsSecuencial : IGeneradorIds
{
    private readonly string _prefijo;
    private int _contador;

    public GeneradorIdsSecuencial(string prefijo = "id")
    {
        _prefijo = prefijo;
    }

    public string NuevoId()
    {
        return $"{_prefijo}-{Interlocked.Increment(ref _contador):D4}";
    }
}
