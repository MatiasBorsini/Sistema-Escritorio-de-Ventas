namespace LocalInfantil.Application.Features.CuentaCorriente;

public static class PermisosCuentaCorriente
{
    public const string Consultar = "cuenta_corriente.consultar";
    public const string VenderFiado = "cuenta_corriente.vender_fiado";
    public const string Abonar = "cuenta_corriente.abonar";
    public const string Ajustar = "cuenta_corriente.ajustar";
    public const string ConfigurarLimite = "cuenta_corriente.configurar_limite";
    public static IReadOnlyList<string> Todos { get; } = [Consultar, VenderFiado, Abonar, Ajustar, ConfigurarLimite];
}
