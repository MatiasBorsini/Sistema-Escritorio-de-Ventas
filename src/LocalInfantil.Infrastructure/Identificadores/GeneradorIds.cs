using LocalInfantil.Application.Abstractions;

namespace LocalInfantil.Infrastructure.Identificadores;

/// <summary>Generador real de identificadores (GUID sin guiones, como el esquema V1).</summary>
public sealed class GeneradorIds : IGeneradorIds
{
    public string NuevoId() => Guid.NewGuid().ToString("N");
}
