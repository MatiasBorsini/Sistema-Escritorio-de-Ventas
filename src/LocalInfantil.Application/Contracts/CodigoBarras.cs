namespace LocalInfantil.Application.Contracts;

/// <summary>Representación técnica independiente de UI para un barcode monocromo.</summary>
public sealed record MatrizCodigoBarras(int Ancho, int Alto, byte[] Modulos)
{
    public bool EstaNegro(int x, int y) => Modulos[checked(y * Ancho + x)] != 0;
}
