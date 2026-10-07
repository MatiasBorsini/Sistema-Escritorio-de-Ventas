using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using ZXing;
using ZXing.Common;

namespace LocalInfantil.Infrastructure.Printing;

/// <summary>Code128 con quiet zone; produce módulos binarios, no depende de WPF ni de un driver.</summary>
public sealed class GeneradorCodigoBarrasCode128 : IGeneradorCodigoBarras
{
    public MatrizCodigoBarras GenerarCodigoBarras(string codigoInterno)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoInterno);
        if (codigoInterno.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '-')))
            throw new ArgumentException("El código interno contiene caracteres no admitidos.", nameof(codigoInterno));

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 10 };
        var matriz = new MultiFormatWriter().encode(codigoInterno, BarcodeFormat.CODE_128, 0, 1, hints);
        var bits = new byte[checked(matriz.Width * matriz.Height)];
        for (var y = 0; y < matriz.Height; y++)
            for (var x = 0; x < matriz.Width; x++)
                bits[y * matriz.Width + x] = matriz[x, y] ? (byte)1 : (byte)0;
        return new MatrizCodigoBarras(matriz.Width, matriz.Height, bits);
    }
}
