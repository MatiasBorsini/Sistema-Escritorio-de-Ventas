using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Tests.Fakes;

/// <summary>Reloj controlable para pruebas: devuelve siempre la hora fijada.</summary>
public sealed class RelojFijo : IReloj
{
    private DateTimeOffset _ahora;

    public RelojFijo(DateTimeOffset ahora)
    {
        _ahora = ahora;
    }

    public DateTimeOffset UtcNow() => _ahora;

    public void Avanzar(TimeSpan paso)
    {
        _ahora = _ahora.Add(paso);
    }
}
