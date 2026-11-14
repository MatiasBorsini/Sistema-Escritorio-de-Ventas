using System.Text;

namespace LocalInfantil.Application.Features.Catalogo;

/// <summary>
/// Normalización de Talle/Color para la regla "sin duplicados por producto":
/// trim; NULL y cadena vacía son el mismo "sin valor"; comparación
/// case-insensitive con invariante (Unicode habitual: á/Á, ñ/Ñ).
/// El índice único en persistencia es el backstop exacto; esta es la
/// autoridad con mensaje humano.
/// </summary>
public static class NormalizacionVariante
{
    /// <summary>Normaliza un atributo: "" significa "sin valor".</summary>
    public static string NormalizarAtributo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return string.Empty;
        }

        return valor.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>Igualdad lógica de combinaciones Talle+Color.</summary>
    public static bool MismaCombinacion(string? talleA, string? colorA, string? talleB, string? colorB)
        => NormalizarAtributo(talleA) == NormalizarAtributo(talleB)
            && NormalizarAtributo(colorA) == NormalizarAtributo(colorB);
}
