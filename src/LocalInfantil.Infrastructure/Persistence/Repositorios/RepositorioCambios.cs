using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

public sealed class RepositorioCambios(IGeneradorIds ids) : IRepositorioCambios
{
    public async Task<bool> ExisteOperacionAsync(ISesionPersistencia sesion,string operacionId,CancellationToken ct=default)
    {
        var s=Sesion(sesion); using var cmd=Comando(s,"SELECT 1 FROM cambio WHERE operacion_id=$op LIMIT 1;"); cmd.Parameters.AddWithValue("$op",operacionId); return await cmd.ExecuteScalarAsync(ct) is not null;
    }
    public async Task<bool> ExistePorVentaAsync(ISesionPersistencia sesion,string ventaId,CancellationToken ct=default)
    {
        var s=Sesion(sesion); using var cmd=Comando(s,"SELECT 1 FROM cambio WHERE venta_id=$venta LIMIT 1;"); cmd.Parameters.AddWithValue("$venta",ventaId); return await cmd.ExecuteScalarAsync(ct) is not null;
    }
    public async Task<long> CantidadDisponibleAsync(ISesionPersistencia sesion, string ventaLineaId, CancellationToken ct = default)
    {
        var s = Sesion(sesion); using var cmd = Comando(s, "SELECT l.cantidad-COALESCE(SUM(e.cantidad),0) FROM venta_linea l LEFT JOIN cambio_entrada e ON e.venta_linea_id=l.id WHERE l.id=$id GROUP BY l.id;");
        cmd.Parameters.AddWithValue("$id", ventaLineaId); var value = await cmd.ExecuteScalarAsync(ct);
        return value is null ? 0 : Convert.ToInt64(value);
    }

    public async Task<int> ObtenerSiguienteNumeroAsync(ISesionPersistencia sesion, CancellationToken ct = default)
    {
        var s = Sesion(sesion); ExigirEscritura(s); using var read = Comando(s, "SELECT siguiente_numero FROM secuencia_cambio WHERE id=1;");
        var number = Convert.ToInt32(await read.ExecuteScalarAsync(ct)); using var update = Comando(s, "UPDATE secuencia_cambio SET siguiente_numero=siguiente_numero+1 WHERE id=1;");
        await update.ExecuteNonQueryAsync(ct); return number;
    }

    public async Task<string> InsertarCambioAsync(ISesionPersistencia sesion, string numero, DatosCambio datos, string usuarioId, string? autorizadoPorId, long importeEntradas, long importeSalidas, string fecha, CancellationToken ct = default)
    {
        var s=Sesion(sesion); ExigirEscritura(s); var id=ids.NuevoId(); using var cmd=Comando(s,"INSERT INTO cambio(id,numero,operacion_id,venta_id,usuario_id,autorizado_reintegro_por_usuario_id,creado_en,importe_entradas_minor,importe_salidas_minor,diferencia_minor,medio_reintegro,motivo,decision_cuenta,deuda_antes_minor,importe_aplicado_deuda_minor,deuda_despues_minor) VALUES($id,$numero,$op,$venta,$usuario,$autorizante,$fecha,$entradas,$salidas,$diff,$medio,$motivo,$decision,$deudaAntes,$aplicado,$deudaDespues);");
        var aplica=datos.DecisionCuenta==DecisionCuentaCorrienteCambio.AplicarADeuda?Math.Min(Math.Max(0,importeEntradas-importeSalidas),datos.DeudaCuentaEsperadaMinor??0):0;
        cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$numero",numero); cmd.Parameters.AddWithValue("$op",datos.OperacionId); cmd.Parameters.AddWithValue("$venta",datos.VentaId); cmd.Parameters.AddWithValue("$usuario",usuarioId); cmd.Parameters.AddWithValue("$autorizante",(object?)autorizadoPorId??DBNull.Value); cmd.Parameters.AddWithValue("$fecha",fecha); cmd.Parameters.AddWithValue("$entradas",importeEntradas); cmd.Parameters.AddWithValue("$salidas",importeSalidas); cmd.Parameters.AddWithValue("$diff",importeSalidas-importeEntradas); cmd.Parameters.AddWithValue("$medio",(object?)datos.MedioReintegro??DBNull.Value); cmd.Parameters.AddWithValue("$motivo",(object?)datos.Motivo?.Trim()??DBNull.Value); cmd.Parameters.AddWithValue("$decision",(object?)(datos.DecisionCuenta?.ToString() switch { "AplicarADeuda" => "aplicar_a_deuda", "Reintegrar" => "reintegrar", _ => null })??DBNull.Value); cmd.Parameters.AddWithValue("$deudaAntes",(object?)datos.DeudaCuentaEsperadaMinor??DBNull.Value); cmd.Parameters.AddWithValue("$aplicado",aplica); cmd.Parameters.AddWithValue("$deudaDespues",(object?)(datos.DeudaCuentaEsperadaMinor.HasValue?datos.DeudaCuentaEsperadaMinor.Value-aplica:null)??DBNull.Value); await cmd.ExecuteNonQueryAsync(ct); return id;
    }

    public async Task InsertarEntradaAsync(ISesionPersistencia sesion,string cambioId,LineaVentaRegistrada linea,LineaCambioEntrada entrada,CancellationToken ct=default)
    {
        var s=Sesion(sesion); ExigirEscritura(s); using var cmd=Comando(s,"INSERT INTO cambio_entrada(id,cambio_id,venta_linea_id,variante_id,cantidad,precio_unitario_minor,apta,motivo_no_apta,codigo_interno,nombre_producto,talle,color) VALUES($id,$cambio,$linea,$variante,$cantidad,$precio,$apta,$motivo,$codigo,$nombre,$talle,$color);");
        cmd.Parameters.AddWithValue("$id",ids.NuevoId()); cmd.Parameters.AddWithValue("$cambio",cambioId); cmd.Parameters.AddWithValue("$linea",linea.Id); cmd.Parameters.AddWithValue("$variante",linea.VarianteId); cmd.Parameters.AddWithValue("$cantidad",entrada.Cantidad); cmd.Parameters.AddWithValue("$precio",linea.PrecioUnitarioMinor); cmd.Parameters.AddWithValue("$apta",entrada.Apta?1:0); cmd.Parameters.AddWithValue("$motivo",(object?)entrada.MotivoNoApta?.Trim()??DBNull.Value); cmd.Parameters.AddWithValue("$codigo",linea.CodigoInterno); cmd.Parameters.AddWithValue("$nombre",linea.NombreProducto); cmd.Parameters.AddWithValue("$talle",(object?)linea.Talle??DBNull.Value); cmd.Parameters.AddWithValue("$color",(object?)linea.Color??DBNull.Value); await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertarSalidaAsync(ISesionPersistencia sesion,string cambioId,string varianteId,string codigo,string nombre,string? talle,string? color,long cantidad,long precio,CancellationToken ct=default)
    {
        var s=Sesion(sesion); ExigirEscritura(s); using var cmd=Comando(s,"INSERT INTO cambio_salida(id,cambio_id,variante_id,cantidad,precio_unitario_minor,codigo_interno,nombre_producto,talle,color) VALUES($id,$cambio,$variante,$cantidad,$precio,$codigo,$nombre,$talle,$color);");
        cmd.Parameters.AddWithValue("$id",ids.NuevoId()); cmd.Parameters.AddWithValue("$cambio",cambioId); cmd.Parameters.AddWithValue("$variante",varianteId); cmd.Parameters.AddWithValue("$cantidad",cantidad); cmd.Parameters.AddWithValue("$precio",precio); cmd.Parameters.AddWithValue("$codigo",codigo); cmd.Parameters.AddWithValue("$nombre",nombre); cmd.Parameters.AddWithValue("$talle",(object?)talle??DBNull.Value); cmd.Parameters.AddWithValue("$color",(object?)color??DBNull.Value); await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string> InsertarPagoAsync(ISesionPersistencia sesion,string cambioId,string direccion,string medio,long importe,long? recibido,CancellationToken ct=default)
    {
        var s=Sesion(sesion); ExigirEscritura(s); var id=ids.NuevoId(); using var cmd=Comando(s,"INSERT INTO cambio_pago(id,cambio_id,direccion,medio,importe_minor,recibido_minor) VALUES($id,$cambio,$direccion,$medio,$importe,$recibido);");
        cmd.Parameters.AddWithValue("$id",id); cmd.Parameters.AddWithValue("$cambio",cambioId); cmd.Parameters.AddWithValue("$direccion",direccion); cmd.Parameters.AddWithValue("$medio",medio); cmd.Parameters.AddWithValue("$importe",importe); cmd.Parameters.AddWithValue("$recibido",(object?)recibido??DBNull.Value); await cmd.ExecuteNonQueryAsync(ct); return id;
    }

    public async Task<IReadOnlyList<ResumenCambio>> ListarPorVentaAsync(ISesionPersistencia sesion,string ventaId,CancellationToken ct=default)
    {
        var s=Sesion(sesion); using var cmd=Comando(s,"SELECT c.id,c.numero,v.numero,c.diferencia_minor,c.creado_en,u.nombre,EXISTS(SELECT 1 FROM cambio_salida cs WHERE cs.cambio_id=c.id),c.importe_aplicado_deuda_minor FROM cambio c JOIN venta v ON v.id=c.venta_id JOIN usuario u ON u.id=c.usuario_id WHERE c.venta_id=$id ORDER BY c.creado_en DESC,c.id DESC;"); cmd.Parameters.AddWithValue("$id",ventaId); var result=new List<ResumenCambio>(); await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetInt64(6)!=0,r.GetInt64(7))); return result;
    }

    public async Task<DocumentoTicketCambio?> ObtenerTicketAsync(ISesionPersistencia sesion,string cambioId,CancellationToken ct=default)
    {
        var s=Sesion(sesion); using var cmd=Comando(s,"SELECT c.numero,v.numero,c.creado_en,u.nombre,ua.nombre,c.motivo,c.importe_entradas_minor,c.importe_salidas_minor,c.diferencia_minor,c.id,c.decision_cuenta,c.importe_aplicado_deuda_minor,c.deuda_antes_minor,c.deuda_despues_minor FROM cambio c JOIN venta v ON v.id=c.venta_id JOIN usuario u ON u.id=c.usuario_id LEFT JOIN usuario ua ON ua.id=c.autorizado_reintegro_por_usuario_id WHERE c.id=$id;");cmd.Parameters.AddWithValue("$id",cambioId);string numero,venta,fecha,usuario;string? autorizante,motivo,decision;long entradas,salidas,diferencia,aplicado;long? deudaAntes,deudaDespues;await using(var r=await cmd.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))return null;numero=r.GetString(0);venta=r.GetString(1);fecha=r.GetString(2);usuario=r.GetString(3);autorizante=r.IsDBNull(4)?null:r.GetString(4);motivo=r.IsDBNull(5)?null:r.GetString(5);entradas=r.GetInt64(6);salidas=r.GetInt64(7);diferencia=r.GetInt64(8);decision=r.IsDBNull(10)?null:r.GetString(10);aplicado=r.GetInt64(11);deudaAntes=r.IsDBNull(12)?null:r.GetInt64(12);deudaDespues=r.IsDBNull(13)?null:r.GetInt64(13);}
        var lineas=new List<LineaTicketCambio>();using(var entradasCmd=Comando(s,"SELECT 'entrada',codigo_interno,nombre_producto,talle,color,cantidad,precio_unitario_minor,apta,motivo_no_apta FROM cambio_entrada WHERE cambio_id=$id UNION ALL SELECT 'salida',codigo_interno,nombre_producto,talle,color,cantidad,precio_unitario_minor,1,NULL FROM cambio_salida WHERE cambio_id=$id ORDER BY 1,2;")){entradasCmd.Parameters.AddWithValue("$id",cambioId);await using var r=await entradasCmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var cantidad=r.GetInt64(5);var precio=r.GetInt64(6);lineas.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),cantidad,precio,cantidad*precio,r.GetInt64(7)!=0,r.IsDBNull(8)?null:r.GetString(8)));}}
        var pagos=new List<PagoTicketCambio>();using(var pagoCmd=Comando(s,"SELECT direccion,medio,importe_minor,recibido_minor FROM cambio_pago WHERE cambio_id=$id ORDER BY direccion,medio;")){pagoCmd.Parameters.AddWithValue("$id",cambioId);await using var r=await pagoCmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))pagos.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.IsDBNull(3)?null:r.GetInt64(3)));}
        return new(numero,venta,fecha,usuario,autorizante,motivo,entradas,salidas,diferencia,lineas,pagos,decision,aplicado,deudaAntes,deudaDespues);
    }

    public async Task<IReadOnlyList<ResumenVenta>> BuscarVentasAsync(ISesionPersistencia sesion,string filtro,string? desde,string? hasta,int limite,CancellationToken ct=default)
    {
        var s=Sesion(sesion); using var cmd=Comando(s,"""
            SELECT v.id,v.numero,v.creada_en,v.total_minor,v.estado,u.nombre,EXISTS(SELECT 1 FROM reversion_venta rv WHERE rv.venta_id=v.id),COUNT(DISTINCT l.id)
            FROM venta v LEFT JOIN usuario u ON u.id=v.creado_por_usuario_id LEFT JOIN venta_linea l ON l.venta_id=v.id
            WHERE ($filtro='' OR v.numero LIKE $like OR EXISTS(SELECT 1 FROM venta_linea x WHERE x.venta_id=v.id AND (x.nombre_producto LIKE $like OR x.codigo_interno LIKE $like)))
              AND ($desde IS NULL OR date(v.creada_en)>=date($desde)) AND ($hasta IS NULL OR date(v.creada_en)<=date($hasta))
            GROUP BY v.id ORDER BY v.creada_en DESC,v.id DESC LIMIT $limite;
            """); cmd.Parameters.AddWithValue("$filtro",filtro.Trim()); cmd.Parameters.AddWithValue("$like",$"%{filtro.Trim()}%"); cmd.Parameters.AddWithValue("$desde",(object?)desde??DBNull.Value); cmd.Parameters.AddWithValue("$hasta",(object?)hasta??DBNull.Value); cmd.Parameters.AddWithValue("$limite",Math.Clamp(limite,1,100)); var result=new List<ResumenVenta>(); await using var r=await cmd.ExecuteReaderAsync(ct); while(await r.ReadAsync(ct)) result.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetInt64(6)!=0,r.GetInt32(7))); return result;
    }

    private static ISesionSqlite Sesion(ISesionPersistencia s)=>s as ISesionSqlite??throw new ArgumentException("La sesión no pertenece a SQLite.",nameof(s));
    private static void ExigirEscritura(ISesionSqlite s){if(s.SoloLectura)throw new InvalidOperationException("La sesión es de solo lectura.");}
    private static Microsoft.Data.Sqlite.SqliteCommand Comando(ISesionSqlite s,string sql){var c=s.Conexion.CreateCommand();c.CommandText=sql;c.CommandTimeout=s.TimeoutSegundos;return c;}
}
