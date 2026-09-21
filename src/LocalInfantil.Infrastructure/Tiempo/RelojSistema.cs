using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Tiempo;

/// <summary>Reloj real: fecha y hora UTC del sistema.</summary>
public sealed class RelojSistema : IReloj
{
    public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;
}
