using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Application.Abstractions;

public interface IGeneradorCodigoBarras
{
    MatrizCodigoBarras GenerarCodigoBarras(string codigoInterno);
}
