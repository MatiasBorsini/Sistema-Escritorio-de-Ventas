namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Claves de sección de la navegación superior y mapeo vista→sección.
/// Una sola taxonomía para Dueña y Empleada; la visibilidad la deciden los
/// permisos existentes. Sin router: solo etiqueta la vista actual.
/// </summary>
public static class NavegacionSecciones
{
    public const string Inicio = "Inicio";
    public const string Ventas = "Ventas";
    public const string Caja = "Caja";
    public const string Mercaderia = "Mercaderia";
    public const string Clientes = "Clientes";
    public const string Administracion = "Administracion";
    public const string Ninguna = "";

    public static string ResolverSeccion(object? vista) => ResolverSeccionPorTipo(vista?.GetType());

    public static string ResolverSeccionPorTipo(Type? tipo)
    {
        if (tipo is null)
            return Ninguna;
        if (tipo == typeof(PortadaViewModel))
            return Inicio;
        if (tipo == typeof(PosViewModel)
            || tipo == typeof(VentasConsultaViewModel)
            || tipo == typeof(CambiosViewModel))
            return Ventas;
        if (tipo == typeof(CajaViewModel))
            return Caja;
        if (tipo == typeof(CatalogoViewModel)
            || tipo == typeof(ProveedoresViewModel)
            || tipo == typeof(RecepcionViewModel)
            || tipo == typeof(RecepcionesConsultaViewModel)
            || tipo == typeof(ConteoViewModel)
            || tipo == typeof(ApartadoViewModel))
            return Mercaderia;
        if (tipo == typeof(ClientesViewModel)
            || tipo == typeof(CuentaCorrienteViewModel))
            return Clientes;
        if (tipo == typeof(AdministracionUsuariosViewModel)
            || tipo == typeof(RespaldosViewModel)
            || tipo == typeof(ReportesViewModel)
            || tipo == typeof(ConfiguracionMercadoPagoViewModel)
            || tipo == typeof(ActualizacionesViewModel))
            return Administracion;
        return Ninguna;
    }
}
