namespace LocalInfantil.Application.Features.MercadoPago;

/// <summary>
/// Solo configuración (credencial, terminal, modo). Cobrar en POS usa
/// ventas.confirmar; cancelar en curso va con el flujo POS; reembolsos
/// reutilizan permisos F05/F06 existentes.
/// </summary>
public static class PermisosMercadoPago
{
    public const string Configurar = "mercadopago.configurar";

    public static readonly IReadOnlyList<string> Todos = new[] { Configurar };
}
