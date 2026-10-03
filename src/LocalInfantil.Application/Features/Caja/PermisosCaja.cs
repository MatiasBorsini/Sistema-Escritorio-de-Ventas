namespace LocalInfantil.Application.Features.Caja;

public static class PermisosCaja
{
    public const string Abrir = "caja.abrir";
    public const string Consultar = "caja.consultar";
    public const string Movimiento = "caja.movimiento";
    public const string Cerrar = "caja.cerrar";
    public const string RetiroPropietario = "caja.retiro_propietario";
    public const string CorregirMedio = "caja.corregir_medio";

    public static IReadOnlyList<string> Todos { get; } = [Abrir, Consultar, Movimiento, Cerrar, RetiroPropietario, CorregirMedio];
}
