using System.IO;
using System.Text.Json;
using LocalInfantil.Application.Contracts;

namespace LocalInfantil.Wpf.Infra;

public sealed record RecepcionPendienteDto(
    string OperacionId,
    string ProveedorId,
    string? ComprobanteRef,
    IReadOnlyList<LineaRecepcion> Lineas,
    string CreadoEn);

public static class AlmacenRecepcionPendiente
{
    public static string RutaPendiente(string rutaDevDb) => rutaDevDb + ".pending-recepcion.json";

    public static void Guardar(string rutaDevDb, string operacionId, string proveedorId, string? comprobanteRef, IReadOnlyList<LineaRecepcion> lineas)
    {
        var ruta = RutaPendiente(rutaDevDb);
        // No sobrescribir un pendiente existente no reconciliado con otro operacionId
        if (File.Exists(ruta))
        {
            var existente = CargarSinBorrar(ruta);
            if (existente is not null && existente.OperacionId != operacionId)
                throw new InvalidOperationException($"Existe una recepción pendiente no reconciliada con operacion_id {existente.OperacionId}. Reconciliá antes de iniciar otra.");
            if (existente is null)
                throw new InvalidOperationException("Existe un archivo pendiente corrupto o ilegible. No se puede iniciar otra recepción hasta reconciliar o eliminar el archivo corrupto.");
        }
        var dto = new RecepcionPendienteDto(operacionId, proveedorId, comprobanteRef, lineas, DateTime.UtcNow.ToString("o"));
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        var tmp = ruta + ".tmp";
        // Secuencia segura: escribir tmp con Flush(true) y luego Move atómico
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        File.Move(tmp, ruta, overwrite: true);
        // Confirmar en disco
        using (var fs2 = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None))
        {
            fs2.Flush(true);
        }
    }

    private static RecepcionPendienteDto? CargarSinBorrar(string ruta)
    {
        try
        {
            var json = File.ReadAllText(ruta);
            var dto = JsonSerializer.Deserialize<RecepcionPendienteDto>(json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.OperacionId) || string.IsNullOrWhiteSpace(dto.ProveedorId) || dto.Lineas is null)
                throw new InvalidDataException("Formato inválido.");
            return dto;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException("Archivo pendiente corrupto, ilegible o parcialmente escrito.", ex);
        }
    }

    public static RecepcionPendienteDto? Cargar(string rutaDevDb)
    {
        var ruta = RutaPendiente(rutaDevDb);
        if (!File.Exists(ruta)) return null;
        return CargarSinBorrar(ruta);
    }

    public static void Eliminar(string rutaDevDb)
    {
        var ruta = RutaPendiente(rutaDevDb);
        if (!File.Exists(ruta)) return;
        File.Delete(ruta);
    }

    public static bool Existe(string rutaDevDb) => File.Exists(RutaPendiente(rutaDevDb));
}
