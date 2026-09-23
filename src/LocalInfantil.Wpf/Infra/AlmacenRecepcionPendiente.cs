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
        // No sobrescribir un pendiente existente no reconciliado: ni con otro
        // operacionId, ni con el mismo operacionId pero distinto contenido.
        if (File.Exists(ruta))
        {
            var existente = CargarSinBorrar(ruta);
            if (existente is null)
                throw new InvalidOperationException("Existe un archivo pendiente corrupto o ilegible. No se puede iniciar otra recepción hasta reconciliar o eliminar el archivo corrupto.");
            if (!string.Equals(existente.OperacionId, operacionId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Existe una recepción pendiente no reconciliada con operacion_id {existente.OperacionId}. Reconciliá antes de iniciar otra.");
            if (!MismoContenido(existente, proveedorId, comprobanteRef, lineas))
                throw new InvalidOperationException($"Ya existe un pendiente con operacion_id {operacionId} pero con otro contenido (proveedor, comprobante o líneas). Reconciliá el pendiente antes de reutilizar el identificador; nunca se reutiliza un identificador con otro contenido.");
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

    private static bool MismoContenido(RecepcionPendienteDto existente, string proveedorId, string? comprobanteRef, IReadOnlyList<LineaRecepcion> lineas)
    {
        if (!string.Equals(existente.ProveedorId, proveedorId, StringComparison.Ordinal)) return false;
        if (!string.Equals(existente.ComprobanteRef, comprobanteRef, StringComparison.Ordinal)) return false;
        if (existente.Lineas.Count != lineas.Count) return false;
        for (var i = 0; i < lineas.Count; i++)
        {
            var a = existente.Lineas[i];
            var b = lineas[i];
            if (!string.Equals(a.VarianteId, b.VarianteId, StringComparison.Ordinal)) return false;
            if (a.Cantidad != b.Cantidad) return false;
            if (a.CostoUnitarioMinor != b.CostoUnitarioMinor) return false;
            if (!string.Equals(a.Moneda, b.Moneda, StringComparison.Ordinal)) return false;
        }
        return true;
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
