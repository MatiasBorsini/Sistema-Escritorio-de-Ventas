using System.Text.Json;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Infrastructure.MercadoPago;

/// <summary>
/// Configuración NO secreta en Secrets/mercadopago.json (fuera de la DB
/// comercial). Nunca contiene el token.
/// </summary>
public sealed class AlmacenConfigMercadoPago : IAlmacenConfigMercadoPago
{
    private readonly string _ruta;

    public AlmacenConfigMercadoPago(string dirSecretos)
    {
        _ruta = Path.Combine(dirSecretos, "mercadopago.json");
    }

    public ConfigMercadoPago Leer()
    {
        try
        {
            if (!File.Exists(_ruta)) return new ConfigMercadoPago(null, null, null, null, null, null);
            var config = JsonSerializer.Deserialize<ConfigMercadoPago>(File.ReadAllText(_ruta),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return config ?? new ConfigMercadoPago(null, null, null, null, null, null);
        }
        catch
        {
            return new ConfigMercadoPago(null, null, null, null, null, null);
        }
    }

    public void Guardar(ConfigMercadoPago config)
    {
        var dir = Path.GetDirectoryName(_ruta);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _ruta + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config));
        File.Move(tmp, _ruta, overwrite: true);
    }
}
