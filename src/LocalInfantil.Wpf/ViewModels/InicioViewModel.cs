namespace LocalInfantil.Wpf.ViewModels;

public sealed class InicioViewModel : ViewModelBase
{
    public string MensajePrincipal => "La navegación del prototipo está lista.";

    public string MensajeSecundario => "Esta pantalla muestra información ficticia en memoria; no se conectó a una base de datos ni a servicios externos.";

    public IReadOnlyList<string> PuntosDeDemostracion { get; } =
    [
        "Modo de trabajo: sin conexión",
        "Datos locales: todavía no configurados",
        "Módulos comerciales: todavía no implementados"
    ];
}
