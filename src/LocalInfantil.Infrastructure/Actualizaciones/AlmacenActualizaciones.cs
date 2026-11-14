using System.Text.Json;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Infrastructure.Actualizaciones;

/// <summary>
/// Persistencia del updater bajo Updates/: canal.json, staging/*.partial,
/// listos/, anterior/. Todo fuera de la DB comercial. Las rutas siempre se
/// contienen dentro de la raíz (anti path traversal).
/// </summary>
public sealed class AlmacenActualizaciones : IAlmacenActualizaciones
{
    private readonly string _raiz;
    private readonly string _dirStaging;
    private readonly string _dirListos;
    private readonly string _dirAnterior;
    private readonly string _rutaConfig;
    private readonly string _dirLocksRespaldo;

    public AlmacenActualizaciones(string raizUpdates, string dirLocksRespaldo)
    {
        _raiz = Path.GetFullPath(raizUpdates);
        _dirStaging = Path.Combine(_raiz, "staging");
        _dirListos = Path.Combine(_raiz, "listos");
        _dirAnterior = Path.Combine(_raiz, "anterior");
        _rutaConfig = Path.Combine(_raiz, "canal.json");
        _dirLocksRespaldo = dirLocksRespaldo;
        Directory.CreateDirectory(_dirStaging);
        Directory.CreateDirectory(_dirListos);
        Directory.CreateDirectory(_dirAnterior);
    }

    public ConfigActualizacion LeerConfiguracion()
    {
        try
        {
            if (!File.Exists(_rutaConfig)) return new ConfigActualizacion(null, null, null);
            var texto = File.ReadAllText(_rutaConfig);
            var config = JsonSerializer.Deserialize<ConfigActualizacion>(texto,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return config ?? new ConfigActualizacion(null, null, null);
        }
        catch
        {
            return new ConfigActualizacion(null, null, null);
        }
    }

    public void GuardarConfiguracion(ConfigActualizacion config)
    {
        var tmp = _rutaConfig + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config));
        File.Move(tmp, _rutaConfig, overwrite: true);
    }

    private string Contener(string baseDir, string nombre)
    {
        var ruta = Path.GetFullPath(Path.Combine(baseDir, nombre));
        if (!ruta.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nombre de archivo inválido.");
        return ruta;
    }

    public string RutaStaging(string nombreArchivo) => Contener(_dirStaging, nombreArchivo + ".partial");
    public string RutaListo(string nombreArchivo) => Contener(_dirListos, nombreArchivo);

    public void PromoverAListo(string rutaParcial, string nombreArchivo)
    {
        var destino = Contener(_dirListos, nombreArchivo);
        if (!string.Equals(Path.GetFullPath(rutaParcial), Contener(_dirStaging, nombreArchivo + ".partial"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta de staging inválida.");
        File.Move(rutaParcial, destino, overwrite: true);
    }

    public void LimpiarParciales()
    {
        foreach (var parcial in Directory.EnumerateFiles(_dirStaging, "*.partial"))
        {
            try { File.Delete(parcial); } catch { }
        }
    }

    public void RecuperarAlIniciar() => LimpiarParciales();

    public IReadOnlyList<string> InstaladoresListos()
    {
        try { return Directory.EnumerateFiles(_dirListos, "*.exe").OrderBy(f => f).ToList(); }
        catch { return Array.Empty<string>(); }
    }

    public IReadOnlyList<ActualizacionListaPersistida> LeerActualizacionesListas()
    {
        var resultado = new List<ActualizacionListaPersistida>();
        foreach (var ruta in InstaladoresListos())
        {
            var manifiesto = ruta + ".manifest.json";
            try
            {
                if (File.Exists(manifiesto))
                    resultado.Add(new ActualizacionListaPersistida(ruta, File.ReadAllText(manifiesto)));
            }
            catch { }
        }
        return resultado;
    }

    public void GuardarManifiestoListo(string rutaInstalador, string manifiestoJson)
    {
        var rutaCompleta = Path.GetFullPath(rutaInstalador);
        if (!InstaladoresListos().Any(x => string.Equals(Path.GetFullPath(x), rutaCompleta, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("El instalador no pertenece al almacenamiento de actualizaciones.");
        var destino = rutaCompleta + ".manifest.json";
        var temporal = destino + ".tmp";
        File.WriteAllText(temporal, manifiestoJson);
        File.Move(temporal, destino, overwrite: true);
    }

    public string? InstaladorAnterior()
    {
        try { return Directory.EnumerateFiles(_dirAnterior).OrderBy(f => f).LastOrDefault(); }
        catch { return null; }
    }

    public void ConservarAnterior(string rutaInstaladorActual)
    {
        try
        {
            foreach (var viejo in Directory.EnumerateFiles(_dirAnterior))
            {
                try { File.Delete(viejo); } catch { }
            }
            var destino = Path.Combine(_dirAnterior, Path.GetFileName(rutaInstaladorActual));
            File.Copy(rutaInstaladorActual, destino, overwrite: true);
        }
        catch { }
    }

    public bool HayOperacionRespaldoEnCurso()
    {
        try
        {
            return Directory.Exists(_dirLocksRespaldo) &&
                Directory.EnumerateFiles(_dirLocksRespaldo, "*.lock").Any();
        }
        catch
        {
            return true;
        }
    }
}
