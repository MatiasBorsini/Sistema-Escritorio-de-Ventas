using System.Collections.ObjectModel;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Wpf.ViewModels;

public sealed class FilaConteoSeleccionViewModel : ViewModelBase
{
    private string _sku = string.Empty;
    private string _cantidadTexto = "0";
    private string? _varianteId;
    private string? _detalle;
    private MotorSugerencias<SugerenciaVariante>? _motorVariantes;
    public string Sku
    {
        get => _sku;
        set
        {
            if (SetProperty(ref _sku, value) && MotorVariantes is not null && MotorVariantes.Texto != value)
                MotorVariantes.EstablecerTexto(value);
        }
    }
    public string CantidadTexto { get => _cantidadTexto; set => SetProperty(ref _cantidadTexto, value); }
    public string? VarianteId { get => _varianteId; set => SetProperty(ref _varianteId, value); }
    public string? Detalle { get => _detalle; set => SetProperty(ref _detalle, value); }

    /// <summary>Buscador de la fila (lo provee el VM padre). Null = solo texto.</summary>
    public MotorSugerencias<SugerenciaVariante>? MotorVariantes
    {
        get => _motorVariantes;
        set
        {
            if (ReferenceEquals(_motorVariantes, value)) return;
            if (_motorVariantes is not null)
                _motorVariantes.PropertyChanged -= Motor_TextoCambiado;
            SetProperty(ref _motorVariantes, value);
            if (_motorVariantes is not null)
            {
                _motorVariantes.PropertyChanged += Motor_TextoCambiado;
                if (_motorVariantes.Texto != _sku)
                    _motorVariantes.EstablecerTexto(_sku);
            }
        }
    }

    public RelayCommand SeleccionarSugerenciaCommand { get; }

    public FilaConteoSeleccionViewModel()
    {
        SeleccionarSugerenciaCommand = new RelayCommand(p =>
        {
            if (p is SugerenciaVariante s)
                AplicarSugerencia(s);
        });
    }

    /// <summary>Aplica una sugerencia con el código/id real (Crear/Corregir validan igual).</summary>
    public void AplicarSugerencia(SugerenciaVariante s)
    {
        Sku = s.CodigoInterno;
        VarianteId = s.VarianteId;
        Detalle = $"{s.CodigoInterno} {s.DetalleTalleColor}";
    }

    /// <summary>Puente motor→fila: lo tipeado en el buscador es el código.</summary>
    private void Motor_TextoCambiado(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MotorSugerencias<SugerenciaVariante>.Texto)
            && _motorVariantes is not null && _motorVariantes.Texto != _sku)
        {
            _sku = _motorVariantes.Texto;
            OnPropertyChanged(nameof(Sku));
        }
    }
}

public sealed class ConteoViewModel : ViewModelBase
{
    private readonly ServiciosComerciales _servicios;
    private readonly Func<string?> _idSesion;
    private string _operacionId = string.Empty;
    private string? _error;
    private string? _mensaje;
    private bool _ocupado;
    private ConteoRegistrado? _seleccionado;
    private IReadOnlyList<InformeConteoLinea> _informe = Array.Empty<InformeConteoLinea>();
    private bool _confirmacionAprobarVisible;
    private int _desplazamiento;
    private const int LimiteValor = 20;
    private int _pasoActual = 1;

    public ConteoViewModel(ServiciosComerciales servicios, Func<string?> idSesion)
    {
        _servicios = servicios;
        _idSesion = idSesion;
        _operacionId = servicios.GeneradorIds.NuevoId();
        Conteos = new ObservableCollection<ConteoRegistrado>();
        FilasSeleccion = new ObservableCollection<FilaConteoSeleccionViewModel> { CrearFilaSeleccion() };
        CrearCommand = new AsyncRelayCommand(CrearAsync);
        CorregirCommand = new AsyncRelayCommand(CorregirAsync);
        AprobarCommand = new AsyncRelayCommand(AprobarAsync);
        SolicitarAprobarCommand = new AsyncRelayCommand(SolicitarAprobarAsync);
        CancelarAprobarCommand = new AsyncRelayCommand(CancelarAprobarAsync);
        RefrescarCommand = new AsyncRelayCommand(CargarConteosAsync);
        SiguienteCommand = new AsyncRelayCommand(SiguienteAsync);
        AnteriorCommand = new AsyncRelayCommand(AnteriorAsync);
        AgregarFilaCommand = new AsyncRelayCommand(() => { FilasSeleccion.Add(CrearFilaSeleccion()); return Task.CompletedTask; });
        QuitarFilaCommand = new AsyncRelayCommand(() => { if (FilasSeleccion.Count > 1) FilasSeleccion.RemoveAt(FilasSeleccion.Count - 1); return Task.CompletedTask; });
        SiguientePasoCommand = new AsyncRelayCommand(SiguientePasoAsync);
        AnteriorPasoCommand = new AsyncRelayCommand(() => { if (PasoActual > 1) PasoActual--; return Task.CompletedTask; });
        IrAPasoCommand = new RelayCommand(IrAPaso);
    }

    /// <summary>Fábrica de filas con buscador asistido ya cableado.</summary>
    private FilaConteoSeleccionViewModel CrearFilaSeleccion()
    {
        return new FilaConteoSeleccionViewModel
        {
            MotorVariantes = new MotorSugerencias<SugerenciaVariante>((t, ct) =>
                _servicios.ConsultasCatalogo.BuscarSugerenciasVariantesAsync(t, 10, _idSesion(), ct)),
        };
    }

    /// <summary>
    /// Flujo progresivo 1-4 (seleccionar → contar → revisar → aprobar).
    /// Solo se muestra la etapa principal. Atrás libre; adelante con
    /// validación; clic en stepper solo permite retroceder (no salta
    /// validaciones).
    /// </summary>
    public int PasoActual
    {
        get => _pasoActual;
        private set
        {
            if (value < 1 || value > 4 || value == _pasoActual) return;
            _pasoActual = value;
            OnPropertyChanged(nameof(PasoActual));
            OnPropertyChanged(nameof(Paso1Activo));
            OnPropertyChanged(nameof(Paso2Activo));
            OnPropertyChanged(nameof(Paso3Activo));
            OnPropertyChanged(nameof(Paso4Activo));
            OnPropertyChanged(nameof(Paso1Listo));
            OnPropertyChanged(nameof(Paso2Listo));
            OnPropertyChanged(nameof(Paso3Listo));
            OnPropertyChanged(nameof(Paso1Texto));
            OnPropertyChanged(nameof(Paso2Texto));
            OnPropertyChanged(nameof(Paso3Texto));
            OnPropertyChanged(nameof(Paso4Texto));
            OnPropertyChanged(nameof(Paso1Visible));
            OnPropertyChanged(nameof(Paso2Visible));
            OnPropertyChanged(nameof(Paso3Visible));
            OnPropertyChanged(nameof(Paso4Visible));
        }
    }

    public bool Paso1Activo => PasoActual == 1;
    public bool Paso2Activo => PasoActual == 2;
    public bool Paso3Activo => PasoActual == 3;
    public bool Paso4Activo => PasoActual == 4;
    public bool Paso1Listo => PasoActual > 1;
    public bool Paso2Listo => PasoActual > 2;
    public bool Paso3Listo => PasoActual > 3;

    /// <summary>Etiquetas del stepper con ✓ (binding directo, sin triggers).</summary>
    public string Paso1Texto => (Paso1Listo ? "✓" : "1") + " · Seleccionar";
    public string Paso2Texto => (Paso2Listo ? "✓" : "2") + " · Contar";
    public string Paso3Texto => (Paso3Listo ? "✓" : "3") + " · Revisar";
    public string Paso4Texto => "4 · Aprobar";
    public bool Paso1Visible => PasoActual == 1;
    public bool Paso2Visible => PasoActual == 2;
    public bool Paso3Visible => PasoActual == 3;
    public bool Paso4Visible => PasoActual == 4;

    /// <summary>Resumen del paso 4, derivado del informe ya cargado.</summary>
    public int VariantesContadas => Informe.Count;
    public int SinDiferencia => Informe.Count(i => i.Diferencia == 0);
    public int ConAjuste => Informe.Count(i => i.Diferencia != 0);

    public AsyncRelayCommand SiguientePasoCommand { get; }
    public AsyncRelayCommand AnteriorPasoCommand { get; }
    public RelayCommand IrAPasoCommand { get; }

    public Task SiguientePasoAsyncPublic() => SiguientePasoAsync();

    private void IrAPaso(object? paso)
    {
        int numero = paso is string s && int.TryParse(s, out var n) ? n : paso is int i ? i : 0;
        if (numero < 1 || numero > 4) return;
        if (numero < PasoActual)
        {
            Error = null;
            PasoActual = numero;
        }
        else if (numero > PasoActual)
        {
            Error = "Completá el paso actual para continuar (Atrás/Continuar).";
        }
    }

    private async Task SiguientePasoAsync()
    {
        if (Ocupado) return;
        Error = null;
        switch (PasoActual)
        {
            case 1:
                if (!FilasSeleccion.Any(f => !string.IsNullOrWhiteSpace(f.Sku)))
                {
                    Error = "Agregá al menos una variante (código). Lo no seleccionado queda fuera del conteo.";
                    return;
                }

                PasoActual = 2;
                break;
            case 2:
                await CrearAsync();
                if (Error is null && Seleccionado is not null)
                    PasoActual = 3;
                break;
            case 3:
                if (Seleccionado is null)
                {
                    Error = "Seleccioná un borrador para revisar.";
                    return;
                }

                PasoActual = 4;
                break;
        }
    }

    public ObservableCollection<ConteoRegistrado> Conteos { get; }
    public ObservableCollection<FilaConteoSeleccionViewModel> FilasSeleccion { get; }
    public ConteoRegistrado? Seleccionado
    {
        get => _seleccionado;
        set { if (SetProperty(ref _seleccionado, value) && value is not null) _ = CargarInformeAsync(); }
    }
    public IReadOnlyList<InformeConteoLinea> Informe
    {
        get => _informe;
        set
        {
            if (SetProperty(ref _informe, value))
            {
                OnPropertyChanged(nameof(VariantesContadas));
                OnPropertyChanged(nameof(SinDiferencia));
                OnPropertyChanged(nameof(ConAjuste));
            }
        }
    }
    public string OperacionId { get => _operacionId; set => SetProperty(ref _operacionId, value); }
    public string? Error { get => _error; set => SetProperty(ref _error, value); }
    public string? Mensaje { get => _mensaje; set => SetProperty(ref _mensaje, value); }
    public bool Ocupado { get => _ocupado; set => SetProperty(ref _ocupado, value); }
    public bool ConfirmacionAprobarVisible { get => _confirmacionAprobarVisible; set => SetProperty(ref _confirmacionAprobarVisible, value); }

    public AsyncRelayCommand CrearCommand { get; }
    public AsyncRelayCommand CorregirCommand { get; }
    public AsyncRelayCommand AprobarCommand { get; }
    public AsyncRelayCommand SolicitarAprobarCommand { get; }
    public AsyncRelayCommand CancelarAprobarCommand { get; }
    public AsyncRelayCommand RefrescarCommand { get; }
    public AsyncRelayCommand SiguienteCommand { get; }
    public AsyncRelayCommand AnteriorCommand { get; }
    public int Desplazamiento => _desplazamiento;
    public int LimitePagina => LimiteValor;
    public AsyncRelayCommand AgregarFilaCommand { get; }
    public AsyncRelayCommand QuitarFilaCommand { get; }

    public async Task CargarConteosAsync()
    {
        Error = null;
        try
        {
            var lista = await _servicios.ConsultasConteos.ListarAsync(LimiteValor, _desplazamiento, _idSesion());
            Conteos.Clear();
            foreach (var c in lista) Conteos.Add(c);
            Mensaje = $"{lista.Count} conteo(s) (página {_desplazamiento / LimiteValor + 1}, desplazamiento {_desplazamiento}).";
            OnPropertyChanged(nameof(Desplazamiento));
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public async Task SiguienteAsync()
    {
        _desplazamiento += LimiteValor;
        await CargarConteosAsync();
    }

    public async Task AnteriorAsync()
    {
        if (_desplazamiento >= LimiteValor) _desplazamiento -= LimiteValor;
        else _desplazamiento = 0;
        await CargarConteosAsync();
    }

    private async Task CargarInformeAsync()
    {
        if (Seleccionado is null) return;
        try
        {
            Informe = await _servicios.ConsultasConteos.InformeAsync(Seleccionado.Id, _idSesion());
        }
        catch (Exception ex) { Error = MensajeError(ex); }
    }

    public async Task CrearAsync()
    {
        if (Ocupado) return;
        Error = null; Mensaje = null;
        var filas = new List<FilaConteo>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in FilasSeleccion)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant < 0) { Error = $"Cantidad inválida para código {f.Sku}. Entero >=0."; return; }
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"Código interno no encontrado: {f.Sku}."; return; }
                if (!vistos.Add(vari.Id)) { Error = $"Variante duplicada: {f.Sku}."; return; }
                f.VarianteId = vari.Id;
                f.Detalle = $"{vari.CodigoInterno} {vari.Talle ?? ""}/{vari.Color ?? ""}".Trim();
                filas.Add(new FilaConteo(vari.Id, cant));
            }
            catch (Exception ex) { Error = MensajeError(ex); return; }
        }
        if (filas.Count == 0) { Error = "Agregá al menos una variante."; return; }
        Ocupado = true;
        try
        {
            var id = await _servicios.CasoCrearConteo.EjecutarAsync(filas, _operacionId, _idSesion());
            Mensaje = $"Borrador creado con {filas.Count} variante(s). Parcial: no elegidas quedan fuera.";
            _operacionId = _servicios.GeneradorIds.NuevoId();
            OnPropertyChanged(nameof(OperacionId));
            await CargarConteosAsync();
            Seleccionado = Conteos.FirstOrDefault(c => c.Id == id);
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    public async Task CorregirAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un borrador."; return; }
        if (Seleccionado.Estado != "borrador") { Error = "Solo borrador admite corrección."; return; }
        if (Ocupado) return;
        Error = null; Mensaje = null;
        // Usar Informe para obtener variantes del conteo y tomar cantidades de FilasSeleccion
        var filas = new List<FilaConteo>();
        foreach (var f in FilasSeleccion)
        {
            if (string.IsNullOrWhiteSpace(f.Sku)) continue;
            if (!long.TryParse(f.CantidadTexto.Trim(), out var cant) || cant < 0) { Error = $"Cantidad inválida {f.Sku}."; return; }
            try
            {
                var vari = await _servicios.ConsultasCatalogo.BuscarPorCodigoInternoAsync(f.Sku.Trim(), _idSesion());
                if (vari is null) { Error = $"Código interno no encontrado {f.Sku}."; return; }
                filas.Add(new FilaConteo(vari.Id, cant));
            }
            catch (Exception ex) { Error = MensajeError(ex); return; }
        }
        if (filas.Count == 0) { Error = "Nada para corregir."; return; }
        Ocupado = true;
        try
        {
            await _servicios.CasoCorregirConteo.EjecutarAsync(Seleccionado.Id, filas, _idSesion());
            Mensaje = "Borrador corregido.";
            await CargarInformeAsync();
        }
        catch (Exception ex) { Error = MensajeError(ex); }
        finally { Ocupado = false; }
    }

    private Task SolicitarAprobarAsync()
    {
        if (Seleccionado is null) { Error = "Seleccioná un borrador."; return Task.CompletedTask; }
        if (Seleccionado.Estado != "borrador") { Error = "Ya aprobado."; return Task.CompletedTask; }
        Error = null; Mensaje = null;
        ConfirmacionAprobarVisible = true;
        return Task.CompletedTask;
    }
    private Task CancelarAprobarAsync() { ConfirmacionAprobarVisible = false; return Task.CompletedTask; }

    public async Task AprobarAsync()
    {
        if (Seleccionado is null || !ConfirmacionAprobarVisible) return;
        if (Ocupado) return;
        Error = null; Mensaje = null; Ocupado = true;
        try
        {
            var opAjustes = _servicios.GeneradorIds.NuevoId();
            await _servicios.CasoAprobarConteo.EjecutarAsync(Seleccionado.Id, opAjustes, _idSesion());
            Mensaje = "Conteo aprobado. Diferencias aplicadas como ajustes compensatorios; diferencia cero sin movimiento.";
            ConfirmacionAprobarVisible = false;
            await CargarConteosAsync();
            await CargarInformeAsync();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("foto vieja") || msg.Contains("cambió desde el conteo"))
                Error = msg + " Generá un conteo nuevo; no se actualiza la existencia esperada silenciosamente.";
            else if (msg.Contains("ya fue aprobado"))
                Error = "Doble aprobación bloqueada, incluso tras reapertura.";
            else
                Error = MensajeError(ex);
        }
        finally { Ocupado = false; }
    }

    // Wrappers para pruebas
    public Task SiguienteAsyncPublic() => SiguienteAsync();
    public Task AnteriorAsyncPublic() => AnteriorAsync();
    public Task CrearAsyncPublic() => CrearAsync();
    public Task CorregirAsyncPublic() => CorregirAsync();
    public Task AprobarAsyncPublic() => AprobarAsync();
    public Task SolicitarAprobarAsyncPublic() => SolicitarAprobarAsync();
    public Task CancelarAprobarAsyncPublic() => CancelarAprobarAsync();

    private static string MensajeError(Exception ex) => ex is LocalInfantil.Application.Abstractions.AccesoDenegadoException ? "Permiso denegado." : ex is LocalInfantil.Application.Abstractions.SesionInvalidaException ? "Sesión inválida." : ex.Message;
}
