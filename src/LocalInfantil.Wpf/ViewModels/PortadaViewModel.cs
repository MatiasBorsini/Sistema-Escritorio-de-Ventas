namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Pantalla inicial mínima y honesta (manual §5): bienvenida, cuenta activa
/// y ayuda corta. Sin métricas inventadas ni accesos a funciones inexistentes.
/// </summary>
public sealed class PortadaViewModel : ViewModelBase
{
    public PortadaViewModel(string nombreUsuario, string rol)
    {
        NombreUsuario = nombreUsuario;
        Rol = rol;
    }

    public string NombreUsuario { get; }

    public string Rol { get; }

    public string Bienvenida => $"Hola, {NombreUsuario}.";

    public string NotaRol => Rol == "duena"
        ? "Podés gestionar usuarios y permisos comerciales desde Administración."
        : "Algunas funciones requieren rol Dueña; pedile a la Dueña que habilite tus permisos.";
}
