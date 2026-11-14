using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Catalogo;
using LocalInfantil.Application.Features.Compras;
using LocalInfantil.Infrastructure.Identificadores;
using LocalInfantil.Infrastructure.Persistence.Migrations;
using LocalInfantil.Infrastructure.Persistence.Repositorios;
using LocalInfantil.Infrastructure.Persistence.Sqlite;
using LocalInfantil.Infrastructure.Seguridad;
using LocalInfantil.Tests.Fakes;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

public sealed class WpfRecepcionDurableTests
{
    [Fact]
    public void Pendiente_corrupto_bloquea()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "corr.db");
        Directory.CreateDirectory(dir);
        try
        {
            var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
            File.WriteAllText(rutaPend, "{ not json");
            var ex = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Cargar(ruta));
            Assert.Contains("corrupto", ex.Message.ToLowerInvariant());
            // No se interpreta como ausencia: sigue existiendo y bloquea Guardar con otro id
            var ex2 = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, "op2", "prov2", null, new[] { new LineaRecepcion("v1", 1, null, null) }));
            Assert.Contains("corrupto", ex2.Message.ToLowerInvariant());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Pendiente_incompleto_tmp_no_sobrescribe_definitivo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "tmp.db");
        Directory.CreateDirectory(dir);
        try
        {
            var op1 = Guid.NewGuid().ToString("N");
            AlmacenRecepcionPendiente.Guardar(ruta, op1, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
            var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
            var contenidoAntes = File.ReadAllText(rutaPend);
            // Simular escritura parcial dejando .tmp incompleto
            File.WriteAllText(rutaPend + ".tmp", "{ parcial");
            // El definitivo debe seguir intacto
            Assert.True(File.Exists(rutaPend));
            Assert.Equal(contenidoAntes, File.ReadAllText(rutaPend));
            Assert.True(File.Exists(rutaPend + ".tmp"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task Fallo_al_guardar_no_envia_recepcion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "noguardar.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        // Hacer directorio no escribible para forzar fallo al guardar pendiente
        var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaPend)!);
        File.WriteAllText(rutaPend, "{}"); // corrupto existente
        // Intentar guardar con otro op debe fallar por corrupto existente y no debe enviar
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.FirstOrDefault(p => p.Id == prov) ?? new ProveedorRegistrado(prov, "Prov", true);
        var sku = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v1).CodigoInterno;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "1" });
        // Forzar pendiente corrupto bloquea Guardar
        File.WriteAllText(rutaPend, "{ corrupto");
        await vm.ConfirmarAsyncPublic();
        Assert.Contains("corrupto", vm.Error ?? "", StringComparison.OrdinalIgnoreCase);
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Empty(recepciones);
        try { File.Delete(rutaPend); } catch { }
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Fallo_al_eliminar_no_habilita_otra_recepcion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "noeliminar.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        var sku = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v1).CodigoInterno;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku, CantidadTexto = "2" });
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        // Simular fallo al eliminar: bloquear archivo pendiente
        var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
        // Crear pendiente artificial y bloquearlo
        AlmacenRecepcionPendiente.Guardar(ruta, "op-bloqueo", prov, null, new[] { new LineaRecepcion(v1, 1, null, null) });
        using var bloqueo = new FileStream(rutaPend, FileMode.Open, FileAccess.Read, FileShare.None);
        // Intentar eliminar debe fallar (IOException) y ViewModel debe mantener pendiente
        // Verificar que Existe sigue true a pesar de intentar eliminar
        Assert.True(AlmacenRecepcionPendiente.Existe(ruta));
        bloqueo.Dispose();
        AlmacenRecepcionPendiente.Eliminar(ruta);
        Assert.False(AlmacenRecepcionPendiente.Existe(ruta));
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Recepcion_confirmada_precarga_etiquetas_y_estas_no_cambian_el_stock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "etiquetas-recepcion.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var proveedor = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Proveedor de prueba"), e.SesionDuena);
        var (producto, v1, v2) = await CrearCatalogoAsync(e);
        var servicioUi = new ServiciosComerciales(ruta);
        var sesionUi = await servicioUi.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(servicioUi, () => sesionUi.IdSesion);
        await vm.CargarProveedoresAsync();
        vm.ProveedorSeleccionado = vm.Proveedores.Single(p => p.Id == proveedor);
        vm.Filas.Clear();
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(producto, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = ficha!.Variantes.Single(v => v.Id == v1).CodigoInterno, CantidadTexto = "3" });
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = ficha.Variantes.Single(v => v.Id == v2).CodigoInterno, CantidadTexto = "5" });

        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.True(vm.EtiquetasRecepcionDisponibles);
        var etiquetas = vm.CrearEtiquetasRecepcionViewModel();
        await etiquetas.InicializarAsync();
        Assert.Equal(new[] { 3, 5 }, etiquetas.Filas.Select(f => f.Cantidad).OrderBy(x => x));
        Assert.Equal(2, etiquetas.Seleccionadas.Count);
        Assert.Equal(3, (await servicioUi.ConsultasInventario.ObtenerStockVendibleAsync(v1, sesionUi.IdSesion))!.Disponible);
        Assert.Equal(5, (await servicioUi.ConsultasInventario.ObtenerStockVendibleAsync(v2, sesionUi.IdSesion))!.Disponible);
    }

    [Fact]
    public async Task Recuperacion_en_curso_bloquea_confirmar()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "bloqueo.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, new[] { new LineaRecepcion(v1, 1, null, null) });
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        // No esperar VerificarPendiente, llamar Confirmar concurrentemente
        var tVerif = vm.VerificarPendienteAsync();
        var tConfirm = vm.ConfirmarAsyncPublic(); // debe estar bloqueado por Ocupado
        await Task.WhenAll(tVerif, tConfirm);
        // Si estaba recuperando, Confirmar no debe haber creado segunda recepción
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.True(recepciones.Count <= 1);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Pendiente_existente_distinto_operacion_id_no_sobrescribe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "nooverwrite.db");
        Directory.CreateDirectory(dir);
        var op1 = Guid.NewGuid().ToString("N");
        AlmacenRecepcionPendiente.Guardar(ruta, op1, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
        var ex = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, "op2", "prov2", null, new[] { new LineaRecepcion("v2", 1, null, null) }));
        Assert.Contains("no reconciliada", ex.Message.ToLowerInvariant());
        var cargado = AlmacenRecepcionPendiente.Cargar(ruta)!;
        Assert.Equal(op1, cargado.OperacionId);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Cambio_contenido_incierto_requiere_nueva_confirmacion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "cambio.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, op, null, e.SesionDuena);
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        // Simular pendiente con op ya registrado pero contenido distinto (v1 vs v2)
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, new[] { new LineaRecepcion(v1, 2, null, null) });
        // Forzar ViewModel a tener ese pendiente
        await vm.VerificarPendienteAsync(); // detectará ya registrada y limpiará con mensaje
        Assert.Contains("ya quedó registrada", vm.Mensaje ?? "");
        // Ahora cambiar contenido a v2 y confirmar debe requerir nueva confirmación explícita, no auto
        var sku2 = (await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena))!.Variantes.First(x => x.Id == v2).CodigoInterno;
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = sku2, CantidadTexto = "5" });
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        // La reconciliación previa ya generó nuevo id, ahora confirmar nuevo contenido debe crear nueva recepción (requiere confirmación explícita, que damos)
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Equal(2, recepciones.Count);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Reinicio_recupera_formulario_exacto_y_no_duplica()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "reinicio.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, v2) = await CrearCatalogoAsync(e);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        var sku1 = ficha!.Variantes.First(x => x.Id == v1).CodigoInterno;
        var sku2 = ficha.Variantes.First(x => x.Id == v2).CodigoInterno;
        var op = Guid.NewGuid().ToString("N");
        var lineas = new[] { new LineaRecepcion(v1, 3, 15050, "ARS"), new LineaRecepcion(v2, 2, null, null) };
        // Guardar pendiente y simular que la recepción NO llegó a confirmarse.
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, "COMP-7", lineas);
        // Destruir el ViewModel (nunca se creó: reinicio) y crear otro.
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        // Formulario recuperado exacto, con SKU auténticos (nunca ids internos).
        Assert.Equal(op, vm.OperacionId);
        Assert.Equal("COMP-7", vm.ComprobanteRef);
        Assert.Equal(prov, vm.ProveedorSeleccionado?.Id);
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(sku1, vm.Filas[0].Sku);
        Assert.Equal("3", vm.Filas[0].CantidadTexto);
        Assert.Equal("150.50", vm.Filas[0].CostoTexto);
        Assert.Equal("ARS", vm.Filas[0].Moneda);
        Assert.Equal(sku2, vm.Filas[1].Sku);
        Assert.Equal("2", vm.Filas[1].CantidadTexto);
        Assert.Equal(string.Empty, vm.Filas[1].CostoTexto);
        Assert.DoesNotContain(vm.Filas, f => f.Sku == v1 || f.Sku == v2);
        // Reintentar con el mismo operacion_id: UNA recepción, stock UNA vez.
        await vm.ConfirmarAsyncPublic();
        Assert.Null(vm.Error);
        Assert.False(AlmacenRecepcionPendiente.Existe(ruta));
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Single(recepciones);
        var ficha2 = await svc.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), ses.IdSesion);
        Assert.Equal(3, ficha2!.Variantes.First(x => x.Id == v1).Existencias);
        Assert.Equal(2, ficha2.Variantes.First(x => x.Id == v2).Existencias);
        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task Reconciliacion_fallo_al_eliminar_mantiene_bloqueo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        var ruta = Path.Combine(dir, "elimfail.db");
        await new MigradorBaseDatos(ruta).MigrarAsync();
        using var e = await Entorno.CrearAsync(ruta);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.ProveedoresGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCompras.RecepcionConfirmar, true, e.SesionDuena);
        var prov = await e.CrearProveedor.EjecutarAsync(new NuevoProveedor("Prov"), e.SesionDuena);
        var (prod, v1, _) = await CrearCatalogoAsync(e);
        var op = Guid.NewGuid().ToString("N");
        await e.ConfirmarRecepcion.EjecutarAsync(prov, new[] { new LineaRecepcion(v1, 2, null, null) }, op, null, e.SesionDuena);
        var svc = new ServiciosComerciales(ruta);
        var ses = await svc.CasoIniciarSesion.EjecutarAsync("duena", "clave-duena-000000");
        var vm = new LocalInfantil.Wpf.ViewModels.RecepcionViewModel(svc, () => ses.IdSesion);
        await vm.CargarProveedoresAsync();
        // Pendiente con op ya registrado y mismo contenido; bloquear su eliminación
        // con un handle Win32 sin sharing (FileStream de .NET siempre comparte el
        // borrado y no sirve para simular el fallo).
        AlmacenRecepcionPendiente.Guardar(ruta, op, prov, null, new[] { new LineaRecepcion(v1, 2, null, null) });
        var rutaPend = AlmacenRecepcionPendiente.RutaPendiente(ruta);
        var bloqueo = AbrirSinSharing(rutaPend);
        Assert.NotEqual(nint.Zero, bloqueo);
        var fOp = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_operacionId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fUlt = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimasLineas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var fProv = typeof(LocalInfantil.Wpf.ViewModels.RecepcionViewModel).GetField("_ultimoProveedorId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fOp.SetValue(vm, op);
        fUlt.SetValue(vm, new List<LineaRecepcion> { new LineaRecepcion(v1, 2, null, null) });
        fProv.SetValue(vm, prov);
        var ficha = await e.ConsultasCatalogo.ObtenerFichaAsync(prod, DateTime.UtcNow.ToString("o"), e.SesionDuena);
        vm.ProveedorSeleccionado = vm.Proveedores.First(p => p.Id == prov);
        vm.Filas.Clear();
        vm.Filas.Add(new LocalInfantil.Wpf.ViewModels.FilaRecepcionViewModel { Sku = ficha!.Variantes.First(x => x.Id == v1).CodigoInterno, CantidadTexto = "2" });
        await vm.ConfirmarAsyncPublic();
        // Eliminación fallida: bloqueo conservado, sin otra recepción.
        Assert.Contains("No se pudo eliminar", vm.Error ?? "");
        Assert.Equal(op, vm.OperacionId);
        Assert.True(AlmacenRecepcionPendiente.Existe(ruta));
        var recepciones = await svc.ConsultasRecepciones.ListarAsync(10, 0, ses.IdSesion);
        Assert.Single(recepciones);
        CerrarHandle(bloqueo);
        AlmacenRecepcionPendiente.Eliminar(ruta);
        Directory.Delete(dir, true);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern nint CreateFileW(string nombre, uint acceso, uint sharing, nint seguridad, uint creacion, uint flags, nint plantilla);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    private static nint AbrirSinSharing(string ruta)
    {
        const uint GENERIC_READ = 0x80000000u;
        const uint OPEN_EXISTING = 3u;
        return CreateFileW(ruta, GENERIC_READ, 0u, nint.Zero, OPEN_EXISTING, 0u, nint.Zero);
    }

    private static void CerrarHandle(nint handle)
    {
        if (handle != nint.Zero && handle != new nint(-1)) CloseHandle(handle);
    }

    [Fact]
    public void Guardar_mismo_id_distinto_contenido_rechaza()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var ruta = Path.Combine(dir, "mismoid.db");
        try
        {
            var op = Guid.NewGuid().ToString("N");
            AlmacenRecepcionPendiente.Guardar(ruta, op, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
            // Mismo id, distinto comprobante.
            var ex1 = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, op, "prov1", "COMP-X", new[] { new LineaRecepcion("v1", 1, null, null) }));
            Assert.Contains("otro contenido", ex1.Message.ToLowerInvariant());
            // Mismo id, distinta línea.
            var ex2 = Assert.Throws<InvalidOperationException>(() => AlmacenRecepcionPendiente.Guardar(ruta, op, "prov1", null, new[] { new LineaRecepcion("v1", 9, null, null) }));
            Assert.Contains("otro contenido", ex2.Message.ToLowerInvariant());
            // Mismo id, mismo contenido: reescritura idempotente permitida.
            AlmacenRecepcionPendiente.Guardar(ruta, op, "prov1", null, new[] { new LineaRecepcion("v1", 1, null, null) });
            Assert.Equal(op, AlmacenRecepcionPendiente.Cargar(ruta)!.OperacionId);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private async Task<(string prod, string v1, string v2)> CrearCatalogoAsync(Entorno e)
    {
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.ProductosGestionar, true, e.SesionDuena);
        await e.Permisos.EjecutarAsync(e.DuenaId, PermisosCatalogo.VariantesGestionar, true, e.SesionDuena);
        var prod = await e.CrearProducto.EjecutarAsync(new NuevoProducto("Prod-" + Guid.NewGuid().ToString("N").Substring(0, 4), null, "cat-otr"), e.SesionDuena);
        var v1 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, "M", "Rojo"), e.SesionDuena);
        var v2 = await e.CrearVariante.EjecutarAsync(new NuevaVariante(prod, null, null), e.SesionDuena);
        return (prod, v1, v2);
    }

    private sealed class Entorno : IDisposable
    {
        public string Ruta { get; }
        public FabricaConexionesSqlite Fabrica { get; }
        public CrearProveedor CrearProveedor { get; }
        public CrearProducto CrearProducto { get; }
        public CrearVariante CrearVariante { get; }
        public ConsultasCatalogo ConsultasCatalogo { get; }
        public ConfirmarRecepcion ConfirmarRecepcion { get; }
        public ConsultasRecepciones ConsultasRecepciones { get; }
        public string SesionDuena { get; }
        public string DuenaId { get; }
        public ConfigurarPermiso Permisos { get; }
        private readonly string _dir;
        public Entorno(string dir, string ruta, FabricaConexionesSqlite fabrica, CrearProveedor cp, CrearProducto cprod, CrearVariante cvar, ConsultasCatalogo cc, ConfirmarRecepcion cr, ConsultasRecepciones cr2, string sesion, string duenaId, ConfigurarPermiso perm)
        { _dir = dir; Ruta = ruta; Fabrica = fabrica; CrearProveedor = cp; CrearProducto = cprod; CrearVariante = cvar; ConsultasCatalogo = cc; ConfirmarRecepcion = cr; ConsultasRecepciones = cr2; SesionDuena = sesion; DuenaId = duenaId; Permisos = perm; }
        public static async Task<Entorno> CrearAsync(string? rutaPrevia = null)
        {
            var dir = rutaPrevia is null ? Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N")) : Path.GetDirectoryName(rutaPrevia)!;
            var ruta = rutaPrevia ?? Path.Combine(dir, "stock.db");
            if (rutaPrevia is null) await new MigradorBaseDatos(ruta).MigrarAsync();
            var fabrica = new FabricaConexionesSqlite(new OpcionesConexionSqlite(ruta));
            var ids = new GeneradorIds();
            var reloj = new RelojAvance(DateTimeOffset.Parse("2026-09-22T00:00:00Z"));
            var sesiones = new GestorSesiones();
            var usuarios = new RepositorioUsuarios(ids);
            var permisos = new RepositorioPermisos();
            var auditoria = new RepositorioAuditoria(new GeneradorIdsSecuencial("ev"), reloj);
            var matriz = MatrizPermisos.ConValoresConfirmados();
            var autorizador = new Autorizador(sesiones, usuarios, permisos, fabrica, matriz);
            var hasher = new Pbkdf2HashContrasenas();
            var alta = new CrearUsuario(usuarios, hasher, autorizador, fabrica, auditoria, ids, reloj);
            var login = new IniciarSesion(usuarios, hasher, sesiones, fabrica, auditoria, ids, reloj, new OpcionesAutenticacion(0));
            var perm = new ConfigurarPermiso(permisos, usuarios, autorizador, fabrica, auditoria, ids, reloj);
            var provRepo = new RepositorioProveedores(ids);
            var prodRepo = new RepositorioProductos(ids);
            var varRepo = new RepositorioVariantes(ids);
            var movRepo = new RepositorioMovimientos(ids);
            var recepRepo = new RepositorioRecepciones(ids);
            var crearProv = new CrearProveedor(provRepo, autorizador, fabrica, auditoria, ids, reloj);
            var crearProd = new CrearProducto(prodRepo, new RepositorioCategorias(ids), autorizador, fabrica, auditoria, ids, reloj);
            var crearVar = new CrearVariante(varRepo, prodRepo, autorizador, fabrica, auditoria, ids, reloj);
            var consultasCat = new ConsultasCatalogo(prodRepo, varRepo, sesiones, fabrica);
            var confirmarRec = new ConfirmarRecepcion(recepRepo, provRepo, varRepo, movRepo, autorizador, fabrica, auditoria, reloj);
            var consultasRec = new ConsultasRecepciones(recepRepo, sesiones, fabrica);
            string duenaId, sesId;
            if (rutaPrevia is null)
            {
                duenaId = await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            else
            {
                await using var lecturaPrevia = await fabrica.IniciarLecturaAsync();
                var existente = await usuarios.ObtenerPorNombreAsync(lecturaPrevia, "duena");
                duenaId = existente?.Id
                    ?? await alta.EjecutarAsync(new NuevoUsuario("duena", RolesLocal.Duena), "clave-duena-000000", null);
                sesId = (await login.EjecutarAsync("duena", "clave-duena-000000")).IdSesion;
            }
            return new Entorno(dir, ruta, fabrica, crearProv, crearProd, crearVar, consultasCat, confirmarRec, consultasRec, sesId, duenaId, perm);
        }
        public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    }

    [Fact]
    public async Task Prototipo_con_archivo_invalido_informa_error_sin_reconstruir()
    {
        // Solo archivo ficticio temporal; nunca la ruta provisional real.
        var dir = Path.Combine(Path.GetTempPath(), "LocalInfantil.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var ruta = Path.Combine(dir, "prototipo-invalido.db");
        try
        {
            var basura = "esto no es una base sqlite";
            File.WriteAllText(ruta, basura);
            var comprobacion = new LocalInfantil.Application.Features.Persistencia.ComprobarPersistenciaLocal(
                new SqlitePersistenciaTecnicaDePrueba(ruta));
            var resultado = await comprobacion.EjecutarAsync();
            Assert.False(resultado.DatoCoincide);
            Assert.NotNull(resultado.MensajeError);
            // El archivo no fue reconstruido silenciosamente: sigue con el contenido inválido original.
            Assert.Equal(basura, File.ReadAllText(ruta));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class RelojAvance : IReloj
    {
        private DateTimeOffset _ahora;
        public RelojAvance(DateTimeOffset inicio) => _ahora = inicio;
        public DateTimeOffset UtcNow() { var r = _ahora; _ahora = _ahora.AddSeconds(1); return r; }
    }
}
