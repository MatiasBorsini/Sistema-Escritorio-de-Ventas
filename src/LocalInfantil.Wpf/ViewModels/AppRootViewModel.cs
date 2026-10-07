using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Clientes;
using LocalInfantil.Application.Features.CuentaCorriente;
using LocalInfantil.Application.Features.Respaldos;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class AppRootViewModel : ViewModelBase
{
    private readonly ServiciosComerciales? _servicios;
    private readonly string? _rutaDev;
    private readonly string? _errorInicial;
    private readonly bool _esProduccion;
    private ViewModelBase? _contenido;
    private SesionUsuario? _sesion;

    public AppRootViewModel(ServiciosComerciales? servicios, string? rutaDev, string? errorInicial, ViewModelBase contenidoInicial, bool esProduccion = false)
    {
        _servicios = servicios;
        _rutaDev = rutaDev;
        _errorInicial = errorInicial;
        _esProduccion = esProduccion;
        _contenido = contenidoInicial;
    }

    public ViewModelBase? Contenido
    {
        get => _contenido;
        set => SetProperty(ref _contenido, value);
    }

    public SesionUsuario? Sesion
    {
        get => _sesion;
        set
        {
            if (SetProperty(ref _sesion, value))
            {
                OnPropertyChanged(nameof(EstaAutenticado));
                OnPropertyChanged(nameof(Identidad));
            }
        }
    }

    public bool EstaAutenticado => Sesion is not null;
    public string Identidad => Sesion is null ? string.Empty : $"{Sesion.Nombre} ({Sesion.Rol})";
    public string? ErrorInicial => _errorInicial;
    public string? RutaDev => _rutaDev;
    public bool ModoDesarrollo => _servicios is not null && !_esProduccion;

    public void Mostrar(ViewModelBase vm) => Contenido = vm;

    /// <summary>
    /// Hook de App: al cerrar sesión, Login vive siempre en LoginWindow.
    /// App oculta la principal, abre una LoginWindow nueva y la vuelve a
    /// mostrar (o cierra la app si no hay autenticación). Sin hook asignado
    /// se conserva el fallback histórico (login embebido).
    /// </summary>
    public Func<Task>? AlCerrarSesion { get; set; }

    public async Task CerrarSesionAsync()
    {
        if (_servicios is not null && Sesion is not null)
        {
            try { await _servicios.CasoCerrarSesion.EjecutarAsync(Sesion.IdSesion); } catch { }
        }

        Sesion = null;
        Contenido = new InicioViewModel();
        if (AlCerrarSesion is not null)
        {
            await AlCerrarSesion.Invoke();
            return;
        }

        if (_servicios is not null)
        {
            // fallback sin App (tests): volver a login/bootstrap según estado
            await VerificarEstadoAsync();
        }
    }

    public async Task VerificarEstadoAsync()
    {
        if (_servicios is null) return;
        await using var sesion = await _servicios.Fabrica.IniciarLecturaAsync();
        var total = await _servicios.RepoUsuarios.ContarTodosAsync(sesion);
        if (total == 0)
        {
            Contenido = new BootstrapDuenaViewModel(_servicios, async s => await AutenticadoAsync(s));
        }
        else
        {
            Contenido = new LoginViewModel(_servicios, async s => await AutenticadoAsync(s));
        }
    }

    public async Task AutenticadoAsync(SesionUsuario s)
    {
        Sesion = s;
        await MostrarShellAsync();
    }

    private async Task MostrarShellAsync()
    {
        if (_servicios is null || Sesion is null) return;
        var dialogosCatalogo = new DialogosCatalogoWpf(_servicios, () => Sesion.IdSesion);
        var catalogo = new CatalogoViewModel(_servicios, () => Sesion.IdSesion, dialogosCatalogo);
        var proveedores = new ProveedoresViewModel(_servicios, () => Sesion.IdSesion);
        await proveedores.CargarAsync();
        var recepcion = new RecepcionViewModel(_servicios, () => Sesion.IdSesion);
        await recepcion.CargarProveedoresAsync();
        await recepcion.VerificarPendienteAsync();
        var dialogosRecepciones = new DialogosRecepcionesWpf(_servicios, () => Sesion.IdSesion);
        var recepciones = new RecepcionesConsultaViewModel(_servicios, () => Sesion.IdSesion, dialogosRecepciones);
        await recepciones.CargarAsync();
        var conteo = new ConteoViewModel(_servicios, () => Sesion.IdSesion);
        await conteo.CargarConteosAsync();
        var apartado = new ApartadoViewModel(_servicios, () => Sesion.IdSesion);
        var permisosClientes = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, PermisosClientes.Todos);
        var permisosCuenta = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, PermisosCuentaCorriente.Todos);
        var dialogosVentas = new DialogosVentasWpf(_servicios, () => Sesion.IdSesion);
        var pos = new PosViewModel(_servicios, () => Sesion.IdSesion, dialogosVentas, permisosClientes.Contains(PermisosClientes.Consultar),
            permisosCuenta.Contains(PermisosCuentaCorriente.VenderFiado), permisosCuenta.Contains(PermisosCuentaCorriente.Consultar));
        var ventas = new VentasConsultaViewModel(_servicios, () => Sesion.IdSesion, dialogosVentas);
        var caja = new CajaViewModel(_servicios, () => Sesion.IdSesion);
        await caja.CargarAsyncPublic();
        var permisosRespaldos = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, PermisosRespaldos.Todos);
        RespaldosViewModel? respaldos = permisosRespaldos.Contains(PermisosRespaldos.Consultar)
            ? new RespaldosViewModel(_servicios, () => Sesion.IdSesion, permisosRespaldos, Sesion.Nombre)
            : null;
        if (respaldos is not null) await respaldos.CargarAsync();
        var permisosCambios = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, LocalInfantil.Application.Features.Cambios.PermisosCambios.Todos);
        CambiosViewModel? cambios = permisosCambios.Contains(LocalInfantil.Application.Features.Cambios.PermisosCambios.Consultar)
            ? new CambiosViewModel(_servicios, () => Sesion.IdSesion, true, permisosCambios.Contains(LocalInfantil.Application.Features.Cambios.PermisosCambios.Registrar))
            : null;
        ClientesViewModel? clientes = permisosClientes.Contains(PermisosClientes.Consultar)
            ? new ClientesViewModel(_servicios, () => Sesion.IdSesion, permisosClientes.Contains(PermisosClientes.Administrar), permisosCuenta.Contains(PermisosCuentaCorriente.Consultar))
            : null;
        CuentaCorrienteViewModel? cuentaCorriente = permisosCuenta.Contains(PermisosCuentaCorriente.Consultar)
            ? new CuentaCorrienteViewModel(_servicios, () => Sesion.IdSesion,
                true,
                permisosCuenta.Contains(PermisosCuentaCorriente.Abonar),
                permisosCuenta.Contains(PermisosCuentaCorriente.Ajustar),
                permisosCuenta.Contains(PermisosCuentaCorriente.ConfigurarLimite))
            : null;
        var permisosReportes = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, LocalInfantil.Application.Features.Reportes.PermisosReportes.Todos);
        ReportesViewModel? reportes = permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Ventas)
            || permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Caja)
            || permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Inventario)
            ? new ReportesViewModel(_servicios, () => Sesion.IdSesion,
                permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Ventas),
                permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Caja),
                permisosReportes.Contains(LocalInfantil.Application.Features.Reportes.PermisosReportes.Inventario))
            : null;
        var permisosMp = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, LocalInfantil.Application.Features.MercadoPago.PermisosMercadoPago.Todos);
        ConfiguracionMercadoPagoViewModel? configMp = permisosMp.Contains(LocalInfantil.Application.Features.MercadoPago.PermisosMercadoPago.Configurar)
            ? new ConfiguracionMercadoPagoViewModel(_servicios, () => Sesion.IdSesion)
            : null;
        if (configMp is not null) await configMp.CargarAsync();
        string? avisoMp = null;
        try
        {
            var reconciliacion = await _servicios.CasoReconciliarMercadoPago.EjecutarAsync(Sesion.IdSesion);
            if (reconciliacion.PendientesDeVerificar > 0)
                avisoMp = reconciliacion.HayInternet
                    ? "Hay un cobro de Mercado Pago pendiente de verificar."
                    : "Hay un cobro de Mercado Pago pendiente de verificar. Revisá la conexión.";
        }
        catch { }
        var resumenInicio = await _servicios.ConsultasInicio.ObtenerAsync(Sesion.IdSesion);
        var permisosActualizaciones = await _servicios.Autorizador.ObtenerPermisosEfectivosAsync(Sesion.IdSesion, [LocalInfantil.Application.Features.Actualizaciones.PermisosActualizaciones.Instalar]);
        var actualizaciones = new ActualizacionesViewModel(_servicios, () => Sesion.IdSesion, _esProduccion,
            Sesion.Rol == "duena" && permisosActualizaciones.Contains(LocalInfantil.Application.Features.Actualizaciones.PermisosActualizaciones.Instalar));
        if (Sesion.Rol == "duena")
        {
            var admin = new AdministracionUsuariosViewModel(_servicios, () => Sesion.IdSesion);
            await admin.CargarAsync();
            Contenido = new ShellDuenaViewModel(Sesion, resumenInicio, admin, catalogo, proveedores, recepcion, recepciones, conteo, apartado, CerrarSesionAsync, pos, ventas, caja, cambios, clientes, cuentaCorriente, respaldos, reportes, actualizaciones, configMp, avisoMp);
        }
        else
        {
            Contenido = new ShellEmpleadaViewModel(Sesion, resumenInicio, catalogo, proveedores, recepcion, recepciones, conteo, apartado, CerrarSesionAsync, pos, ventas, caja, cambios, clientes, cuentaCorriente, respaldos, reportes, actualizaciones, avisoMp);
        }
    }
}

public sealed class ShellDuenaViewModel : ViewModelBase
{
    private ViewModelBase _vistaActual;

    public SesionUsuario Sesion { get; }
    public PortadaViewModel Portada { get; }
    public AdministracionUsuariosViewModel Administracion { get; }
    public CatalogoViewModel Catalogo { get; }
    public ProveedoresViewModel Proveedores { get; }
    public RecepcionViewModel Recepcion { get; }
    public RecepcionesConsultaViewModel Recepciones { get; }
    public ConteoViewModel Conteo { get; }
    public ApartadoViewModel Apartado { get; }
    public PosViewModel? Pos { get; }
    public VentasConsultaViewModel? Ventas { get; }
    public CajaViewModel? Caja { get; }
    public CambiosViewModel? Cambios { get; }
    public ClientesViewModel? Clientes { get; }
    public CuentaCorrienteViewModel? CuentaCorriente { get; }
    public RespaldosViewModel? Respaldos { get; }
    public ReportesViewModel? Reportes { get; }
    public ActualizacionesViewModel? Actualizaciones { get; }
    public ConfiguracionMercadoPagoViewModel? ConfigMercadoPago { get; private set; }
    public string? AvisoMercadoPago { get; private set; }
    public Func<Task> CerrarSesion { get; }
    public string Identidad => $"{Sesion.Nombre} ({Sesion.Rol}) — Dueña";
    public string VersionApp => "Versión " + Infra.VersionAplicacion.Obtener();

    public ShellDuenaViewModel(SesionUsuario sesion, AdministracionUsuariosViewModel admin, CatalogoViewModel catalogo, ProveedoresViewModel prov, RecepcionViewModel rec, RecepcionesConsultaViewModel cons, ConteoViewModel conteo, ApartadoViewModel apartado, Func<Task> cerrar)
        : this(sesion, ResumenVacio(), admin, catalogo, prov, rec, cons, conteo, apartado, cerrar)
    {
    }

    public ShellDuenaViewModel(SesionUsuario sesion, LocalInfantil.Application.Contracts.ResumenInicio resumenInicio, AdministracionUsuariosViewModel admin, CatalogoViewModel catalogo, ProveedoresViewModel prov, RecepcionViewModel rec, RecepcionesConsultaViewModel cons, ConteoViewModel conteo, ApartadoViewModel apartado, Func<Task> cerrar, PosViewModel? pos = null, VentasConsultaViewModel? ventas = null, CajaViewModel? caja = null, CambiosViewModel? cambios = null, ClientesViewModel? clientes = null, CuentaCorrienteViewModel? cuentaCorriente = null, RespaldosViewModel? respaldos = null, ReportesViewModel? reportes = null, ActualizacionesViewModel? actualizaciones = null, ConfiguracionMercadoPagoViewModel? configMp = null, string? avisoMp = null)
    {
        Sesion = sesion;
        ConfigMercadoPago = configMp;
        AvisoMercadoPago = avisoMp;
        Administracion = admin;
        Catalogo = catalogo;
        Proveedores = prov;
        Recepcion = rec;
        Recepciones = cons;
        Conteo = conteo;
        Apartado = apartado;
        Pos = pos;
        Ventas = ventas;
        Caja = caja;
        Cambios = cambios;
        Clientes = clientes;
        CuentaCorriente = cuentaCorriente;
        Respaldos = respaldos;
        Reportes = reportes;
        Actualizaciones = actualizaciones;
        Portada = new PortadaViewModel(sesion.Nombre, resumenInicio, new RelayCommand(_ => VistaActual = Recepcion), new AsyncRelayCommand(async () => { VistaActual = Catalogo; await Catalogo.InicializarAsyncPublic(); }), new RelayCommand(_ => VistaActual = Conteo), new RelayCommand(_ => IrAApartado(null)));
        CerrarSesion = cerrar;
        _vistaActual = Portada;
        MostrarVistaCommand = new RelayCommand(MostrarVista);
        IrAApartadoCommand = new RelayCommand(IrAApartado);
    }

    public ViewModelBase VistaActual
    {
        get => _vistaActual;
        private set => SetProperty(ref _vistaActual, value);
    }

    public System.Windows.Input.ICommand MostrarVistaCommand { get; }

    public System.Windows.Input.ICommand IrAApartadoCommand { get; }

    private void MostrarVista(object? vista)
    {
        if (vista is ViewModelBase viewModel)
        {
            VistaActual = viewModel;
        }

        if (vista is CatalogoViewModel catalogo)
        {
            _ = catalogo.InicializarAsyncPublic();
        }
        if (vista is ClientesViewModel clientes)
            _ = clientes.CargarAsync(true);
        if (vista is RespaldosViewModel respaldos)
            _ = respaldos.CargarAsync();
    }

    /// <summary>
    /// Entradas de menú "Consulta de inventario" (sin parámetro, conserva el
    /// estado actual) y "No vendible" (parámetro "Apartar"/"Reincorporar"/"Baja"
    /// preselecciona la acción). La vista es la misma; cambia el punto de entrada.
    /// </summary>
    private void IrAApartado(object? accion)
    {
        if (accion is string nombre)
            Apartado.SeleccionarAccionCommand.Execute(nombre);
        VistaActual = Apartado;
    }

    private static LocalInfantil.Application.Contracts.ResumenInicio ResumenVacio()
        => new(new HashSet<string>(), 0, new(0, 0, 0), Array.Empty<LocalInfantil.Application.Contracts.ActividadInicio>());
}

public sealed class ShellEmpleadaViewModel : ViewModelBase
{
    private ViewModelBase _vistaActual;

    public SesionUsuario Sesion { get; }
    public PortadaViewModel Portada { get; }
    public CatalogoViewModel Catalogo { get; }
    public ProveedoresViewModel Proveedores { get; }
    public RecepcionViewModel Recepcion { get; }
    public RecepcionesConsultaViewModel Recepciones { get; }
    public ConteoViewModel Conteo { get; }
    public ApartadoViewModel Apartado { get; }
    public PosViewModel? Pos { get; }
    public VentasConsultaViewModel? Ventas { get; }
    public CajaViewModel? Caja { get; }
    public CambiosViewModel? Cambios { get; }
    public ClientesViewModel? Clientes { get; }
    public CuentaCorrienteViewModel? CuentaCorriente { get; }
    public RespaldosViewModel? Respaldos { get; }
    public ReportesViewModel? Reportes { get; }
    public ActualizacionesViewModel? Actualizaciones { get; }
    public string? AvisoMercadoPago { get; private set; }
    public Func<Task> CerrarSesion { get; }
    public string Identidad => $"{Sesion.Nombre} ({Sesion.Rol})";
    public string VersionApp => "Versión " + Infra.VersionAplicacion.Obtener();

    public ShellEmpleadaViewModel(SesionUsuario sesion, CatalogoViewModel catalogo, ProveedoresViewModel prov, RecepcionViewModel rec, RecepcionesConsultaViewModel cons, ConteoViewModel conteo, ApartadoViewModel apartado, Func<Task> cerrar)
        : this(sesion, ResumenVacio(), catalogo, prov, rec, cons, conteo, apartado, cerrar)
    {
    }

    public ShellEmpleadaViewModel(SesionUsuario sesion, LocalInfantil.Application.Contracts.ResumenInicio resumenInicio, CatalogoViewModel catalogo, ProveedoresViewModel prov, RecepcionViewModel rec, RecepcionesConsultaViewModel cons, ConteoViewModel conteo, ApartadoViewModel apartado, Func<Task> cerrar, PosViewModel? pos = null, VentasConsultaViewModel? ventas = null, CajaViewModel? caja = null, CambiosViewModel? cambios = null, ClientesViewModel? clientes = null, CuentaCorrienteViewModel? cuentaCorriente = null, RespaldosViewModel? respaldos = null, ReportesViewModel? reportes = null, ActualizacionesViewModel? actualizaciones = null, string? avisoMp = null)
    {
        Sesion = sesion;
        AvisoMercadoPago = avisoMp;
        Catalogo = catalogo;
        Proveedores = prov;
        Recepcion = rec;
        Recepciones = cons;
        Conteo = conteo;
        Apartado = apartado;
        Pos = pos;
        Ventas = ventas;
        Caja = caja;
        Cambios = cambios;
        Clientes = clientes;
        CuentaCorriente = cuentaCorriente;
        Respaldos = respaldos;
        Reportes = reportes;
        Actualizaciones = actualizaciones;
        Portada = new PortadaViewModel(sesion.Nombre, resumenInicio, new RelayCommand(_ => VistaActual = Recepcion), new AsyncRelayCommand(async () => { VistaActual = Catalogo; await Catalogo.InicializarAsyncPublic(); }), new RelayCommand(_ => VistaActual = Conteo), new RelayCommand(_ => IrAApartado(null)));
        CerrarSesion = cerrar;
        _vistaActual = Portada;
        MostrarVistaCommand = new RelayCommand(MostrarVista);
        IrAApartadoCommand = new RelayCommand(IrAApartado);
    }

    public ViewModelBase VistaActual
    {
        get => _vistaActual;
        private set => SetProperty(ref _vistaActual, value);
    }

    public System.Windows.Input.ICommand MostrarVistaCommand { get; }

    public System.Windows.Input.ICommand IrAApartadoCommand { get; }

    private void MostrarVista(object? vista)
    {
        if (vista is ViewModelBase viewModel)
        {
            VistaActual = viewModel;
        }

        if (vista is CatalogoViewModel catalogo)
        {
            _ = catalogo.InicializarAsyncPublic();
        }
        if (vista is ClientesViewModel clientes)
            _ = clientes.CargarAsync(true);
        if (vista is RespaldosViewModel respaldos)
            _ = respaldos.CargarAsync();
    }

    /// <summary>
    /// Entradas de menú "Consulta de inventario" (sin parámetro, conserva el
    /// estado actual) y "No vendible" (parámetro "Apartar"/"Reincorporar"/"Baja"
    /// preselecciona la acción). La vista es la misma; cambia el punto de entrada.
    /// </summary>
    private void IrAApartado(object? accion)
    {
        if (accion is string nombre)
            Apartado.SeleccionarAccionCommand.Execute(nombre);
        VistaActual = Apartado;
    }

    private static LocalInfantil.Application.Contracts.ResumenInicio ResumenVacio()
        => new(new HashSet<string>(), 0, new(0, 0, 0), Array.Empty<LocalInfantil.Application.Contracts.ActividadInicio>());
}
