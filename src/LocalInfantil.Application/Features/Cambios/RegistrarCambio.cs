using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Caja;
using LocalInfantil.Application.Features.Inventario;
using LocalInfantil.Application.Features.Ventas;

namespace LocalInfantil.Application.Features.Cambios;

public sealed class RegistrarCambio(
    IRepositorioCambios cambios, IRepositorioVentas ventas, IRepositorioVariantes variantes,
    IRepositorioProductos productos, IRepositorioPrecios precios, IRepositorioMovimientos movimientos,
    IRepositorioApartados apartados, IRepositorioCaja caja, IAutorizador autorizador,
    IAutorizadorEnSesion autorizadorTx, IFabricaSesionesPersistencia fabrica,
    IRepositorioAuditoria auditoria, IReloj reloj)
{
    public async Task<ResultadoCambio> EjecutarAsync(DatosCambio datos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (datos.UsuarioSesionId is null) throw new SesionInvalidaException();
        ValidarForma(datos);
        var actorPrevio = await autorizador.AutorizarAsync(datos.UsuarioSesionId, PermisosCambios.Registrar, ct);
        var medios = datos.Cobros.Select(p=>p.Medio).Concat(datos.MedioReintegro is null?Array.Empty<string>():[datos.MedioReintegro]).Distinct().ToArray();
        if (medios.Any(m=>!MediosPago.Todos.Contains(m))) throw new ArgumentException("El medio de pago no está admitido.");

        await using var tx=await fabrica.IniciarAsync(ct);
        var actor=await autorizadorTx.AutorizarAsync(tx,datos.UsuarioSesionId,PermisosCambios.Registrar,ct);
        if (actor.Id != actorPrevio.Id) throw new SesionInvalidaException();
        if(await cambios.ExisteOperacionAsync(tx,datos.OperacionId,ct) || await caja.ExisteOperacionAsync(tx,datos.OperacionId,ct)) throw new InvalidOperationException("Esta operación ya fue registrada.");
        var venta=await ventas.ObtenerPorIdAsync(tx,datos.VentaId,ct)??throw new InvalidOperationException("La venta seleccionada ya no existe.");
        if(venta.Estado!="confirmada" || await ventas.ExisteReversionAsync(tx,venta.Id,ct)) throw new InvalidOperationException("No se pueden registrar cambios sobre una venta anulada.");
        var lineas=await ventas.ListarLineasAsync(tx,venta.Id,ct);
        var porId=lineas.ToDictionary(l=>l.Id,StringComparer.Ordinal);
        var fecha=reloj.UtcNow().ToString("o");
        long entradas=0;
        foreach(var entrada in datos.Entradas)
        {
            if(!porId.TryGetValue(entrada.LineaVentaId,out var linea)) throw new InvalidOperationException("Una prenda no pertenece a la venta seleccionada.");
            var cantidad=await cambios.CantidadDisponibleAsync(tx,linea.Id,ct);
            if(entrada.Cantidad>cantidad) throw new InvalidOperationException($"Solo quedan {cantidad} unidades disponibles para devolución de {linea.NombreProducto}.");
            if(entrada.Cantidad<=0) throw new ArgumentException("La cantidad de devolución debe ser positiva.");
            if(!entrada.Apta && string.IsNullOrWhiteSpace(entrada.MotivoNoApta)) throw new ArgumentException("Indicá el motivo de una prenda no apta.");
            entradas=checked(entradas+checked(entrada.Cantidad*linea.PrecioUnitarioMinor));
        }

        var salidas=new List<(LineaCambioSalida Solicitud, VarianteRegistrada Variante, string Nombre, long Precio)>();
        var preciosCambiados=new List<DetallePrecioCambioCambiado>();
        long importeSalidas=0;
        foreach(var salida in datos.Salidas)
        {
            if(salida.Cantidad<=0) throw new ArgumentException("La cantidad de una prenda nueva debe ser positiva.");
            var variante=await variantes.ObtenerPorIdAsync(tx,salida.VarianteId,ct);
            if(variante is null || !variante.Activa) throw new InvalidOperationException("La variante nueva no está activa.");
            var fisico=await movimientos.SumarPorVarianteAsync(tx,variante.Id,ct); var apartado=await apartados.SumarApartadoPorVarianteAsync(tx,variante.Id,ct);
            if(fisico-apartado<salida.Cantidad) throw new InvalidOperationException($"Solo hay {Math.Max(0,fisico-apartado)} unidades disponibles de la variante nueva.");
            var precio=await precios.ObtenerVigenteAsync(tx,variante.Id,fecha,ct)??throw new InvalidOperationException("La variante nueva no tiene un precio vigente.");
            var producto=await productos.ObtenerPorIdAsync(tx,variante.ProductoId,ct);
            salidas.Add((salida,variante,producto?.Nombre??variante.CodigoInterno,precio.ImporteMinor));
            if(precio.ImporteMinor!=salida.PrecioEsperadoMinor)
                preciosCambiados.Add(new(variante.Id,producto?.Nombre??variante.CodigoInterno,salida.PrecioEsperadoMinor,precio.ImporteMinor));
            importeSalidas=checked(importeSalidas+checked(salida.Cantidad*precio.ImporteMinor));
        }
        if(preciosCambiados.Count>0) return new ResultadoCambio.PrecioCambiado(preciosCambiados);
        if(datos.Entradas.Count==0) throw new ArgumentException("Agregá al menos una prenda de la venta original.");

        var diferencia=checked(importeSalidas-entradas);
        if(diferencia>0)
        {
            if(datos.Cobros.Count==0 || datos.Cobros.Sum(p=>p.ImporteMinor)!=diferencia) throw new InvalidOperationException("Los pagos deben cubrir exactamente la diferencia a cobrar.");
            foreach(var pago in datos.Cobros)
            {
                if(pago.ImporteMinor<=0) throw new ArgumentException("Cada importe debe ser mayor que cero.");
                if(pago.Medio==MediosPago.Efectivo && (pago.RecibidoMinor is null || pago.RecibidoMinor<pago.ImporteMinor)) throw new ArgumentException("El efectivo recibido no alcanza la diferencia.");
                if(pago.Medio!=MediosPago.Efectivo && pago.RecibidoMinor is not null) throw new ArgumentException("Solo efectivo admite importe recibido.");
            }
        }
        else if(datos.Cobros.Count>0) throw new InvalidOperationException("No corresponde cargar pagos cuando no hay diferencia a cobrar.");

        var reintegroMedio= datos.MedioReintegro ?? MediosPago.Efectivo;
        string? autorizanteId=null;
        if(diferencia<0)
        {
            _ = await autorizadorTx.AutorizarAsync(tx,datos.UsuarioSesionId,PermisosComerciales.ReintegroAutorizar,ct);
            if(string.IsNullOrWhiteSpace(datos.Motivo)) throw new ArgumentException("El motivo del reintegro es obligatorio.");
            if(reintegroMedio!=MediosPago.Efectivo)
            {
                var autorizante=actor.Rol==RolesLocal.Duena
                    ? await autorizadorTx.AutorizarAsync(tx,datos.UsuarioSesionId,PermisosComerciales.ReintegroMedioExcepcional,ct)
                    : await autorizadorTx.VerificarDuenaAsync(tx,datos.AutorizanteNombre??"",datos.AutorizanteContrasena??"",PermisosComerciales.ReintegroMedioExcepcional,ct);
                autorizanteId=autorizante.Id;
            }
        }
        if(diferencia!=0 && await caja.ObtenerAbiertaAsync(tx,ct) is null) throw new InvalidOperationException("No hay una caja abierta para registrar la diferencia del cambio.");
        // operation_id is unique in the F06 journal and caja ledger.

        var numero=$"C-{await cambios.ObtenerSiguienteNumeroAsync(tx,ct):D6}";
        var cambioId=await cambios.InsertarCambioAsync(tx,numero,datos,actor.Id,autorizanteId,entradas,importeSalidas,fecha,ct);
        foreach(var entrada in datos.Entradas)
        {
            var linea=porId[entrada.LineaVentaId];
            await cambios.InsertarEntradaAsync(tx,cambioId,linea,entrada,ct);
            await movimientos.RegistrarAsync(tx,new(linea.VarianteId,"ingreso",entrada.Cantidad,$"Devolución {numero}",datos.OperacionId,actor.Id),fecha,ct);
            if(!entrada.Apta) await apartados.RegistrarAsync(tx,linea.VarianteId,"apartar",entrada.Cantidad,$"Cambio {numero}: {entrada.MotivoNoApta!.Trim()}",datos.OperacionId,actor.Id,fecha,ct);
        }
        foreach(var salida in salidas)
        {
            await cambios.InsertarSalidaAsync(tx,cambioId,salida.Variante.Id,salida.Variante.CodigoInterno,salida.Nombre,salida.Variante.Talle,salida.Variante.Color,salida.Solicitud.Cantidad,salida.Precio,ct);
            await movimientos.RegistrarAsync(tx,new(salida.Variante.Id,"egreso",-salida.Solicitud.Cantidad,$"Cambio {numero}",datos.OperacionId,actor.Id),fecha,ct);
        }
        string? cajaId=null;
        if(diferencia!=0)
        {
            var sesionCaja=await caja.ObtenerAbiertaAsync(tx,ct)??throw new InvalidOperationException("No hay una caja abierta para registrar la diferencia del cambio.");
            var tipoOperacion=diferencia>0?TiposOperacionCaja.MovimientoManual:TiposOperacionCaja.Reintegro;
            var cajaOp=await caja.InsertarOperacionAsync(tx,new(datos.OperacionId,sesionCaja.Id,tipoOperacion,actor.Id,fecha),ct);
            if(diferencia>0) foreach(var pago in datos.Cobros)
            {
                var id=await cambios.InsertarPagoAsync(tx,cambioId,"cobro",pago.Medio,pago.ImporteMinor,pago.RecibidoMinor,ct);
                await caja.InsertarMovimientoAsync(tx,new(sesionCaja.Id,cajaOp,TiposMovimientoCaja.CambioCobro,pago.ImporteMinor,null,null,pago.Medio,null,$"Diferencia {numero}",actor.Id,null,fecha,id),ct);
            }
            else
            {
                var importe=-diferencia; long? recibido=reintegroMedio==MediosPago.Efectivo?importe:null;
                var id=await cambios.InsertarPagoAsync(tx,cambioId,"reintegro",reintegroMedio,importe,recibido,ct);
                await caja.InsertarMovimientoAsync(tx,new(sesionCaja.Id,cajaOp,TiposMovimientoCaja.CambioReintegro,-importe,null,null,reintegroMedio,null,$"Reintegro {numero}: {datos.Motivo!.Trim()}",actor.Id,null,fecha,id),ct);
            }
            cajaId=sesionCaja.Id;
        }
        await auditoria.RegistrarAsync(tx,new(datos.OperacionId,"cambio.registrado",ActorAuditoria.Usuario,actor.Id,"cambio",cambioId,$"numero={numero}; venta={venta.Numero}; importe_entradas_minor={entradas}; importe_salidas_minor={importeSalidas}; diferencia_minor={diferencia}; caja={cajaId??"sin_movimiento"}"),ct);
        await tx.ConfirmarAsync(ct);
        return new ResultadoCambio.Registrado(cambioId,numero,entradas,importeSalidas,diferencia,fecha);
    }

    private static void ValidarForma(DatosCambio d)
    {
        if(string.IsNullOrWhiteSpace(d.OperacionId)) throw new ArgumentException("La operación necesita identificador.");
        if(d.Entradas is null||d.Salidas is null||d.Cobros is null) throw new ArgumentNullException(nameof(d));
        if(d.Entradas.Select(e=>e.LineaVentaId).Distinct(StringComparer.Ordinal).Count()!=d.Entradas.Count) throw new ArgumentException("No repitas la misma línea original; consolidá cantidades.");
        if(d.Salidas.Select(e=>e.VarianteId).Distinct(StringComparer.Ordinal).Count()!=d.Salidas.Count) throw new ArgumentException("No repitas la misma variante de salida; consolidá cantidades.");
    }
}
