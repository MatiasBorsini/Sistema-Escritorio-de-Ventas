using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Reportes;

namespace LocalInfantil.Application.Features.Inicio;

public sealed class ConsultasInicio
{
    public const string RecibirMercaderia = "compras.recepcion.confirmar";
    public const string NuevoProducto = "catalogo.productos.gestionar";
    public const string ConteoFisico = "inventario.conteo_registrar";
    private static readonly string[] Acciones = [RecibirMercaderia, NuevoProducto, ConteoFisico];
    private readonly IAutorizador _autorizador; private readonly IRepositorioInicio _inicio; private readonly IFabricaSesionesPersistencia _fabrica;
    public ConsultasInicio(IAutorizador autorizador, IRepositorioInicio inicio, IFabricaSesionesPersistencia fabrica) { _autorizador = autorizador; _inicio = inicio; _fabrica = fabrica; }
    public async Task<ResumenInicio> ObtenerAsync(string idSesion, CancellationToken ct = default)
    {
        var solicitados = Acciones.Append(PermisosReportes.Ventas).ToArray();
        var permitidas = await _autorizador.ObtenerPermisosEfectivosAsync(idSesion, solicitados, ct);
        await using var s = await _fabrica.IniciarLecturaAsync(ct);
        var inventario = await _inicio.ObtenerInventarioAsync(s, ct);
        var pendientes = await _inicio.ContarConteosBorradorAsync(s, ct);
        var eventos = await _inicio.ListarActividadAsync(s, 6, ct);
        var recepcion = await _inicio.ObtenerUltimaRecepcionAsync(s, ct);
        VentasDelDiaInicio? ventas = null;
        if (permitidas.Contains(PermisosReportes.Ventas))
        {
            var dia = RangoFechas.Dia(DateOnly.FromDateTime(DateTime.Today));
            ventas = await _inicio.ObtenerVentasDelDiaAsync(s, dia.DesdeUtc, dia.HastaUtc, ct);
        }
        return new ResumenInicio(permitidas, pendientes, inventario,
            eventos.Select(Mapear).Where(x => x is not null).Cast<ActividadInicio>().ToArray(), ventas, recepcion);
    }
    private static ActividadInicio? Mapear(EventoInicioOrigen e) => e.Tipo switch
    {
        "compra.recepcion_confirmada" => new("Recepción confirmada", e.Referencia, e.Unidades, e.ResponsableNombre, e.CreadoEn),
        "compra.recepcion_revertida" => new("Recepción revertida", e.Referencia, e.Unidades, e.ResponsableNombre, e.CreadoEn),
        "inventario.conteo_creado" => new("Conteo físico creado", e.Detalle, null, e.ResponsableNombre, e.CreadoEn),
        "inventario.conteo_corregido" => new("Conteo físico corregido", e.Detalle, null, e.ResponsableNombre, e.CreadoEn),
        "inventario.conteo_aprobado" => new("Conteo físico aprobado", e.Detalle, null, e.ResponsableNombre, e.CreadoEn),
        "inventario.apartado" => new("Mercadería apartada", e.Referencia, e.Unidades, e.ResponsableNombre, e.CreadoEn),
        "inventario.reincorporado" => new("Mercadería reincorporada", e.Referencia, e.Unidades, e.ResponsableNombre, e.CreadoEn),
        "inventario.baja_definitiva" => new("Baja definitiva", e.Referencia, e.Unidades, e.ResponsableNombre, e.CreadoEn), _ => null
    };
}
