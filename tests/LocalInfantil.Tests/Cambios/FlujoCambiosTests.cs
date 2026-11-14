using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Ventas;
using LocalInfantil.Tests.Ventas;
using Xunit;

namespace LocalInfantil.Tests.Cambios;

public sealed class FlujoCambiosTests
{
    [Fact]
    public async Task Diferencia_a_cobrar_es_atomica_parcial_y_bloquea_anulacion_posterior()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();
        var (_,v1,v2)=await e.CatalogoConStockAsync();
        var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);
        var venta=await VenderAsync(e,[(v1,1,p1),(v2,1,p2)]);
        var lineas=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);
        var resultado=await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(lineas!.Lineas.Single(x=>x.VarianteId==v1).Id,1,true,null)],
            [new LineaCambioSalida(v2,1,p2)], [new PagoDiferenciaCambio(MediosPago.Debito,p2-p1,null)],
            null,null,"op-cambio-cobro",e.Sesion));
        var cambio=Assert.IsType<ResultadoCambio.Registrado>(resultado);

        Assert.Equal("C-000001",cambio.Numero);Assert.Equal(p2-p1,cambio.DiferenciaMinor);
        var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(cambio.CambioId,e.Sesion);
        Assert.NotNull(ticket);Assert.Equal("T-000001",ticket!.VentaNumero);Assert.Equal("COMPROBANTE INTERNO — NO FISCAL",ticket.Leyenda);
        Assert.Equal(2,ticket.Lineas.Count);Assert.Contains(ticket.Lineas,x=>x.Direccion=="entrada");Assert.Contains(ticket.Lineas,x=>x.Direccion=="salida");Assert.Single(ticket.Pagos);
        var historial=Assert.Single(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
        Assert.Equal("Cambio",historial.TipoTexto);Assert.Equal("Cobrado",historial.ResultadoTexto);
        var repetida=await Assert.ThrowsAsync<InvalidOperationException>(()=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(lineas.Lineas.Single(x=>x.VarianteId==v1).Id,1,true,null)],[],[],null,null,"op-cambio-cobro",e.Sesion)));
        Assert.Equal("Esta operación ya fue registrada.",repetida.Message);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId,"Error","op-anular-con-cambio",e.Sesion));
        Assert.Contains("cambios o devoluciones",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diferencia_cero_no_requiere_caja_abierta_ni_registra_movimiento_de_caja()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,v2)=await e.CatalogoConStockAsync();var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);var venta=await VenderAsync(e,[(v1,1,p1),(v2,1,p2)]);
        var actual=await e.Servicios.ConsultasCaja.ObtenerActualAsync(e.Sesion);var efectivo=actual!.EfectivoEsperadoMinor??0;var arqueo=await e.Servicios.CasoCerrarCaja.PrevisualizarAsync(efectivo,e.Sesion);
        await e.Servicios.CasoCerrarCaja.EjecutarAsync(actual.Sesion.Id,efectivo,arqueo.EfectivoEsperadoMinor,null,"op-cerrar-previo-cambio",e.Sesion);
        var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);
        var before=await ContarMovimientosAsync(e.Servicios.RutaBaseDatos);
        var resultado=await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            ficha!.Lineas.Select(l=>new LineaCambioEntrada(l.Id,1,true,null)).ToArray(),[new LineaCambioSalida(v1,1,p1),new LineaCambioSalida(v2,1,p2)],[],null,null,"op-cambio-cero",e.Sesion));
        var cambio=Assert.IsType<ResultadoCambio.Registrado>(resultado);
        Assert.Equal(0,cambio.DiferenciaMinor);Assert.Equal(before,await ContarMovimientosAsync(e.Servicios.RutaBaseDatos));
        Assert.True((await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(cambio.CambioId,e.Sesion))!.Lineas.Count==4);
        var historial=Assert.Single(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
        Assert.Equal("Sin diferencia",historial.TipoTexto);Assert.Equal(0,historial.ImporteResultadoMinor);
    }

    [Fact]
    public async Task Devolucion_no_apta_restituye_fisico_pero_queda_apartada_y_reintegra_efectivo()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,_)=await e.CatalogoConStockAsync();var precio=await e.PrecioDeAsync(v1);var venta=await VenderAsync(e,[(v1,1,precio)]);
        var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);
        var resultado=await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(ficha!.Lineas.Single().Id,1,false,"Costura descosida")],[],[],null,"Prenda con defecto","op-cambio-no-apta",e.Sesion));
        var cambio=Assert.IsType<ResultadoCambio.Registrado>(resultado);
        Assert.Equal(-precio,cambio.DiferenciaMinor);
        var stock=await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v1),e.Sesion);
        Assert.Equal(9,stock!.Disponible);Assert.Equal(1,stock.Apartado);Assert.Equal(10,stock.Fisico);
        var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(cambio.CambioId,e.Sesion);
        Assert.Equal("Costura descosida",Assert.Single(ticket!.Lineas).MotivoNoApta);
        Assert.False(Assert.Single(ticket.Lineas).Apta);
        Assert.Equal("No apta · queda apartada",Assert.Single(ticket.Lineas).AptitudTexto);
        Assert.Equal("reintegro",Assert.Single(ticket.Pagos).Direccion);
        var historial=Assert.Single(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
        Assert.Equal("Devolución",historial.TipoTexto);Assert.Equal("Reintegrado",historial.ResultadoTexto);
        var reintegro=Assert.Single(await ObtenerMovimientosCambioAsync(e.Servicios.RutaBaseDatos,TiposMovimientoCaja.CambioReintegro));
        Assert.Equal(-precio,reintegro.ImporteMinor);
        Assert.Empty(await ObtenerMovimientosCambioAsync(e.Servicios.RutaBaseDatos,TiposMovimientoCaja.CambioCobro));
        Assert.Equal("Importe reintegrado",Assert.Single(ticket.Pagos).DireccionTexto);
        Assert.Null(Assert.Single(ticket.Pagos).VueltoMinor);

        var repetida=await Assert.ThrowsAsync<InvalidOperationException>(()=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(
            new DatosCambio(venta.VentaId,[new LineaCambioEntrada(ficha.Lineas.Single().Id,1,false,"Costura descosida")],[],[],null,"Prenda con defecto","op-cambio-no-apta",e.Sesion)));
        Assert.Equal("Esta operación ya fue registrada.",repetida.Message);
        Assert.Single(await ObtenerMovimientosCambioAsync(e.Servicios.RutaBaseDatos,TiposMovimientoCaja.CambioReintegro));
    }

    [Fact]
    public async Task Prenda_apta_persiste_la_aptitud_y_el_comprobante_la_presenta_correctamente()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();
        var (_,variante,_)=await e.CatalogoConStockAsync();
        var precio=await e.PrecioDeAsync(variante);
        var venta=await VenderAsync(e,[(variante,1,precio)]);
        var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);
        var linea=Assert.Single(ficha!.Lineas);

        var registrado=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(
            new DatosCambio(venta.VentaId,[new LineaCambioEntrada(linea.Id,1,true,null)],[],[],null,"Devolución en buen estado","op-cambio-apta",e.Sesion)));

        var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(registrado.CambioId,e.Sesion);
        var entrada=Assert.Single(ticket!.Lineas);
        Assert.True(entrada.Apta);
        Assert.Equal("Apta para volver al inventario disponible",entrada.AptitudTexto);
        var stock=await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(variante),e.Sesion);
        Assert.Equal(10,stock!.Disponible);
        Assert.Equal(0,stock.Apartado);
    }

    [Fact]
    public async Task Precio_salida_cambiado_aborta_sin_escrituras_y_exige_reconfirmar()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,v2)=await e.CatalogoConStockAsync();
        var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);var venta=await VenderAsync(e,[(v1,1,p1)]);
        var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);
        var linea=Assert.Single(ficha!.Lineas);
        await e.Servicios.CasoFijarPrecio.EjecutarAsync(v2,p2+100,e.Sesion);
        var resultado=await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(linea.Id,1,true,null)],[new LineaCambioSalida(v2,1,p2)],[],null,null,"op-cambio-precio-viejo",e.Sesion));
        var precioCambiado=Assert.IsType<ResultadoCambio.PrecioCambiado>(resultado);
        var detalle=Assert.Single(precioCambiado.Cambios);Assert.Equal(p2,detalle.PrecioEsperadoMinor);Assert.Equal(p2+100,detalle.PrecioVigenteMinor);
        Assert.Empty(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
        Assert.Equal(7,(await e.Servicios.ConsultasInventario.ObtenerClasificadaAsync(await e.CodigoDeAsync(v2),e.Sesion))!.Disponible);

        var confirmado=await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(linea.Id,1,true,null)],[new LineaCambioSalida(v2,1,p2+100)],
            [new PagoDiferenciaCambio(MediosPago.Efectivo,p2+100-p1,p2+100)],null,null,"op-cambio-precio-confirmado",e.Sesion));
        Assert.IsType<ResultadoCambio.Registrado>(confirmado);
    }

    [Fact]
    public async Task Devuelve_unidades_parciales_restantes_y_rechaza_la_cuarta()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,_)=await e.CatalogoConStockAsync();var precio=await e.PrecioDeAsync(v1);
        var venta=await VenderAsync(e,[(v1,3,precio)]);var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var linea=Assert.Single(ficha!.Lineas);
        var primero=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,linea.Id,1,"op-retorno-1",e.Sesion)));
        Assert.Equal(-precio,primero.DiferenciaMinor);
        var disponibles=await e.Servicios.ConsultasCambios.ObtenerEntradasDisponiblesAsync(venta.VentaId,e.Sesion);Assert.Equal(2,Assert.Single(disponibles).CantidadDisponible);
        var segundo=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,linea.Id,2,"op-retorno-2",e.Sesion)));
        Assert.Equal(-2*precio,segundo.DiferenciaMinor);
        Assert.Equal(0,Assert.Single(await e.Servicios.ConsultasCambios.ObtenerEntradasDisponiblesAsync(venta.VentaId,e.Sesion)).CantidadDisponible);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,linea.Id,1,"op-retorno-4",e.Sesion)));
        Assert.Contains("Solo quedan 0 unidades disponibles para devolución",error.Message);
        Assert.Equal(2,(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion)).Count);
    }

    [Fact]
    public async Task Dos_operaciones_concurrentes_no_devuelven_dos_veces_la_ultima_unidad()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,_)=await e.CatalogoConStockAsync();var precio=await e.PrecioDeAsync(v1);
        var venta=await VenderAsync(e,[(v1,1,precio)]);var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var linea=Assert.Single(ficha!.Lineas);
        var tareas=new[]{"op-concurrente-a","op-concurrente-b"}.Select(op=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,linea.Id,1,op,e.Sesion))).ToArray();
        var resultados=await Task.WhenAll(tareas.Select(async task=>{try{return await task is ResultadoCambio.Registrado;}catch(InvalidOperationException){return false;}}));
        Assert.Single(resultados.Where(x=>x));
        Assert.Single(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
        Assert.Equal(0,Assert.Single(await e.Servicios.ConsultasCambios.ObtenerEntradasDisponiblesAsync(venta.VentaId,e.Sesion)).CantidadDisponible);
    }

    [Fact]
    public async Task Reintegro_efectivo_permitido_a_empleada_y_duena_puede_autorizar_otro_medio()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var empleado=await e.SesionEmpleadaAsync();var (_,v1,v2)=await e.CatalogoConStockAsync();var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);
        var venta=await VenderAsync(e,[(v1,1,p1),(v2,2,p2)]);var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var lineaV2=ficha!.Lineas.Single(x=>x.VarianteId==v2);
        var empleadoOk=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,lineaV2.Id,1,"op-efectivo-empleada",empleado)));
        Assert.Equal("C-000001",empleadoOk.Numero);
        var lineaV1=ficha.Lineas.Single(x=>x.VarianteId==v1);
        var ownerOk=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
            [new LineaCambioEntrada(lineaV2.Id,1,true,null)],[new LineaCambioSalida(v1,1,p1)],[],MediosPago.Transferencia,"Autorización de Dueña","op-medio-duena",e.Sesion)));
        var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(ownerOk.CambioId,e.Sesion);Assert.Equal("duena",ticket!.Autorizante);
    }

    [Fact]
    public async Task Busqueda_alternativa_por_producto_y_fecha_devuelve_coincidencias_para_seleccion_manual()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,_)=await e.CatalogoConStockAsync();var precio=await e.PrecioDeAsync(v1);
        var venta1=await VenderAsync(e,[(v1,1,precio)]);var venta2=await VenderAsync(e,[(v1,1,precio)]);
        var resultados=await e.Servicios.ConsultasCambios.BuscarVentasAsync("Remera F04",null,null,e.Sesion);
        Assert.True(resultados.Count==2);Assert.Contains(resultados,x=>x.Id==venta1.VentaId);Assert.Contains(resultados,x=>x.Id==venta2.VentaId);
        var porCodigo=await e.Servicios.ConsultasCambios.BuscarVentasAsync(await e.CodigoDeAsync(v1),null,null,e.Sesion);
        Assert.True(porCodigo.Count==2);
    }

    [Fact]
    public async Task Diferencia_positiva_acepta_los_cinco_medios_y_pago_combinado()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,v2)=await e.CatalogoConStockAsync();var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);
        var venta=await VenderAsync(e,[(v1,5,p1)]);var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var linea=Assert.Single(ficha!.Lineas);
        for(var indice=0;indice<MediosPago.Manuales.Count;indice++){var medio=MediosPago.Manuales[indice];
            IReadOnlyList<PagoDiferenciaCambio> pagos=medio==MediosPago.Efectivo
                ?[new(medio,p2-p1,p2-p1+500)]
                :medio==MediosPago.Credito
                    ?[new(MediosPago.Transferencia,(p2-p1)/2,null),new(medio,p2-p1-(p2-p1)/2,null)]
                    :[new(medio,p2-p1,null)];
            var op=$"op-medio-{indice}";
            var result=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(new DatosCambio(venta.VentaId,
                [new LineaCambioEntrada(linea.Id,1,true,null)],[new LineaCambioSalida(v2,1,p2)],pagos,null,null,op,e.Sesion)));
            Assert.Equal(p2-p1,result.DiferenciaMinor);
            var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(result.CambioId,e.Sesion);
            Assert.Equal(pagos.Count,ticket!.Pagos.Count);Assert.Contains(ticket.Pagos,x=>x.Medio==medio);
            if(medio==MediosPago.Efectivo) Assert.Equal(500,ticket.Pagos.Single().VueltoMinor);
        }
    }

    [Fact]
    public async Task Venta_anulada_no_admite_cambio()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var (_,v1,_)=await e.CatalogoConStockAsync();var precio=await e.PrecioDeAsync(v1);var venta=await VenderAsync(e,[(v1,1,precio)]);
        var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var linea=Assert.Single(ficha!.Lineas);
        await e.Servicios.CasoAnularVenta.EjecutarAsync(venta.VentaId,"Venta duplicada","op-anulacion-previa",e.Sesion);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(DatosReintegro(venta.VentaId,linea.Id,1,"op-cambio-anulada",e.Sesion)));
        Assert.Contains("venta anulada",error.Message,StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await e.Servicios.ConsultasCambios.ListarAsync(venta.VentaId,e.Sesion));
    }

    private static DatosCambio DatosReintegro(string ventaId,string lineaId,long cantidad,string operacion,string? sesion)
        =>new(ventaId,[new LineaCambioEntrada(lineaId,cantidad,true,null)],[],[],MediosPago.Efectivo,"Devolución",""+operacion,sesion);

    [Fact]
    public async Task Reintegro_no_efectivo_exige_credencial_presencial_real_de_duena()
    {
        using var e=await VentasTests.EntornoVentas.CrearAsync();var empleado=await e.SesionEmpleadaAsync();var (_,v1,v2)=await e.CatalogoConStockAsync();var p1=await e.PrecioDeAsync(v1);var p2=await e.PrecioDeAsync(v2);var venta=await VenderAsync(e,[(v1,1,p1),(v2,1,p2)]);var ficha=await e.Servicios.ConsultasVentas.ObtenerFichaAsync(venta.VentaId,e.Sesion);var lineav2=ficha!.Lineas.Single(x=>x.VarianteId==v2);
        var datos=new DatosCambio(venta.VentaId,[new LineaCambioEntrada(lineav2.Id,1,true,null)],[new LineaCambioSalida(v1,1,p1)],[],MediosPago.Transferencia,"Cambio solicitado","op-cambio-transferencia",empleado,"duena","clave-incorrecta");
        await Assert.ThrowsAsync<CredencialesInvalidasException>(()=>e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos));
        datos=datos with{AutorizanteContrasena="clave-duena-000000"};
        var result=Assert.IsType<ResultadoCambio.Registrado>(await e.Servicios.CasoRegistrarCambio.EjecutarAsync(datos));var ticket=await e.Servicios.CasoGenerarTicketCambio.EjecutarAsync(result.CambioId,e.Sesion);
        Assert.Equal("duena",ticket!.Autorizante);
    }

    private static async Task<ResultadoConfirmacionVenta.Confirmada> VenderAsync(VentasTests.EntornoVentas e,(string Variante,long Cantidad,long Precio)[] items)
    {
        var total=items.Sum(x=>x.Cantidad*x.Precio);
        return (ResultadoConfirmacionVenta.Confirmada)await e.Servicios.CasoConfirmarVenta.EjecutarAsync(items.Select(x=>new NuevaLineaVenta(x.Variante,x.Cantidad,x.Precio)).ToArray(),[new NuevoPago(MediosPago.Efectivo,total,total)],"op-venta-"+Guid.NewGuid().ToString("N"),e.Sesion);
    }

    private static async Task<long> ContarMovimientosAsync(string ruta)
    {
        await using var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=ruta,Pooling=false,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly}.ToString());await db.OpenAsync();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM caja_movimiento;";return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<IReadOnlyList<(string Tipo,long ImporteMinor)>> ObtenerMovimientosCambioAsync(string ruta,string tipo)
    {
        await using var db=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=ruta,Pooling=false,Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly}.ToString());
        await db.OpenAsync();
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT tipo, importe_minor FROM caja_movimiento WHERE tipo=$tipo ORDER BY creado_en;";
        cmd.Parameters.AddWithValue("$tipo",tipo);
        var filas=new List<(string Tipo,long ImporteMinor)>();
        await using var reader=await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync()) filas.Add((reader.GetString(0),reader.GetInt64(1)));
        return filas;
    }
}
