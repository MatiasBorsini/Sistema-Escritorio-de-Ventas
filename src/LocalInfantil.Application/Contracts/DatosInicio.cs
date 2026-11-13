namespace LocalInfantil.Application.Contracts;

public sealed record ResumenInventarioInicio(long Fisico, long Disponible, long Apartado);
public sealed record EventoInicioOrigen(string Tipo, string? Referencia, long? Unidades, string? Detalle, string? ResponsableNombre, string CreadoEn);
public sealed record ActividadInicio(string Descripcion, string? Referencia, long? Unidades, string? ResponsableNombre, string CreadoEn)
{
    public string Detalle => string.Join(" · ", new[]
    {
        Referencia,
        Unidades is null ? null : $"{Unidades} unidades"
    }.Where(x => !string.IsNullOrWhiteSpace(x))!);

    public string ResponsableYFecha
    {
        get
        {
            var fecha = DateTimeOffset.TryParse(CreadoEn, out var valor)
                ? valor.ToLocalTime().ToString("dd/MM HH:mm")
                : CreadoEn;
            return string.IsNullOrWhiteSpace(ResponsableNombre) ? fecha : $"{ResponsableNombre} · {fecha}";
        }
    }
}
/// <summary>Ventas del día local (importes en minor). NetoMinor ya descuenta anulaciones y cambios.</summary>
public sealed record VentasDelDiaInicio(long NetoMinor, int CantidadVentas);

/// <summary>Última recepción confirmada registrada (dato real, sin ventana temporal inventada).</summary>
public sealed record RecepcionRecienteInicio(string ProveedorNombre, long Unidades, string CreadoEn);

public sealed record ResumenInicio(
    IReadOnlySet<string> AccionesPermitidas,
    int ConteosPendientes,
    ResumenInventarioInicio Inventario,
    IReadOnlyList<ActividadInicio> Actividad,
    VentasDelDiaInicio? VentasDelDia = null,
    RecepcionRecienteInicio? RecepcionReciente = null);
