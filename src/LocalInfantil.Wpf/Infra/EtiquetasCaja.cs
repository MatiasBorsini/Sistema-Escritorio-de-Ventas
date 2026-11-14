using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Infra;

/// <summary>Etiquetas humanas para medios y estados de caja (pantalla e impresión).</summary>
public static class EtiquetasCaja
{
    public static string Humano(string? texto) => texto switch
    {
        null => string.Empty,
        MediosCaja.Efectivo => "Efectivo",
        MediosCaja.Transferencia => "Transferencia",
        MediosCaja.Debito => "Débito",
        MediosCaja.Credito => "Crédito",
        MediosCaja.Billetera => "Billetera virtual",
        MediosPago.MercadoPago => "Mercado Pago",
        "abierta" => "Abierta",
        "cerrada" => "Cerrada",
        "venta" => "Venta",
        "anulacion" => "Anulación",
        "cambio" => "Cambio",
        "devolucion" => "Devolución",
        "ingreso" => "Ingreso",
        "egreso" => "Egreso",
        "reintegro" => "Reintegro",
        "correccion" => "Corrección de medio",
        "confirmada" => "Confirmada",
        "anulada" => "Anulada",
        _ => texto,
    };
}
