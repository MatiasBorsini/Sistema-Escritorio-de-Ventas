using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Infrastructure.Persistence.Sqlite;

namespace LocalInfantil.Infrastructure.Persistence.Repositorios;

/// <summary>Persistencia del libro inmutable de cuenta corriente (V14).</summary>
public sealed class RepositorioCuentaCorriente(IGeneradorIds ids) : IRepositorioCuentaCorriente
{
    public async Task<IReadOnlyList<ClienteCuentaCorrienteListado>> BuscarClientesAsync(ISesionPersistencia sesion, string texto,
        FiltroClientesCuentaCorriente filtro, int limite, int desplazamiento, CancellationToken ct = default)
    {
        if (limite is < 1 or > 50 || desplazamiento < 0) throw new ArgumentOutOfRangeException(nameof(limite));
        var patron = texto.Trim() + "%";
        using var cmd = Crear(sesion, """
            WITH saldo AS (
                SELECT cliente_id, SUM(importe_minor) AS importe
                FROM cuenta_corriente_movimiento GROUP BY cliente_id
            ), datos AS (
                SELECT c.id,c.nombre_completo,c.telefono,COALESCE(s.importe,0) AS saldo,c.limite_credito_minor,c.activo,
                       CASE WHEN c.limite_credito_minor IS NULL THEN 0 ELSE MAX(0,c.limite_credito_minor-COALESCE(s.importe,0)) END AS disponible
                FROM cliente c LEFT JOIN saldo s ON s.cliente_id=c.id
            )
            SELECT id,nombre_completo,telefono,saldo,limite_credito_minor,disponible,activo FROM datos
            WHERE ($texto='' OR nombre_completo LIKE $patron COLLATE NOCASE OR telefono LIKE $patron COLLATE NOCASE)
              AND ($filtro=3 OR ($filtro=0 AND (saldo>0 OR limite_credito_minor IS NOT NULL))
                   OR ($filtro=1 AND saldo>0) OR ($filtro=2 AND activo=1 AND limite_credito_minor IS NOT NULL))
            ORDER BY CASE WHEN $filtro=0 AND saldo>0 THEN 0 WHEN $filtro=0 AND limite_credito_minor IS NOT NULL THEN 1 ELSE 2 END,
                     CASE WHEN saldo>0 THEN saldo ELSE 0 END DESC,nombre_completo COLLATE NOCASE,id
            LIMIT $limite OFFSET $offset;
            """);
        cmd.Parameters.AddWithValue("$texto", texto.Trim()); cmd.Parameters.AddWithValue("$patron", patron);
        cmd.Parameters.AddWithValue("$filtro", (int)filtro); cmd.Parameters.AddWithValue("$limite", limite + 1); cmd.Parameters.AddWithValue("$offset", desplazamiento);
        var rows = new List<ClienteCuentaCorrienteListado>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) rows.Add(new(r.GetString(0), r.GetString(1), N(r, 2), r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetInt64(5), r.GetInt64(6) == 1));
        return rows;
    }

    public async Task<long> ObtenerSaldoAsync(ISesionPersistencia sesion, string clienteId, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, "SELECT COALESCE(SUM(importe_minor),0) FROM cuenta_corriente_movimiento WHERE cliente_id=$cliente;");
        cmd.Parameters.AddWithValue("$cliente", clienteId); return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<CargoCuentaCorriente>> ListarCargosPendientesAsync(ISesionPersistencia sesion, string clienteId, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, SqlCargos + " WHERE m.cliente_id=$cliente AND m.tipo IN ('cargo_venta','ajuste_positivo') AND m.importe_minor-COALESCE(a.aplicado,0)>0 ORDER BY m.creado_en,m.id;");
        cmd.Parameters.AddWithValue("$cliente", clienteId); return await LeerCargos(cmd, ct);
    }

    public async Task<IReadOnlyList<CargoCuentaCorriente>> ListarCargosPendientesVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, SqlCargos + " WHERE m.venta_id=$venta AND m.tipo='cargo_venta' AND m.importe_minor-COALESCE(a.aplicado,0)>0 ORDER BY m.creado_en,m.id;");
        cmd.Parameters.AddWithValue("$venta", ventaId); return await LeerCargos(cmd, ct);
    }

    public async Task<string> InsertarMovimientoAsync(ISesionPersistencia sesion, NuevoMovimientoCuentaCorriente m, CancellationToken ct = default)
    {
        var id = ids.NuevoId();
        using var cmd = Crear(sesion, "INSERT INTO cuenta_corriente_movimiento(id,cliente_id,tipo,importe_minor,venta_id,cambio_id,abono_id,operacion_id,vencimiento,motivo,usuario_id,creado_en) VALUES($id,$cliente,$tipo,$importe,$venta,$cambio,$abono,$op,$vence,$motivo,$usuario,$fecha);");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$cliente", m.ClienteId); cmd.Parameters.AddWithValue("$tipo", m.Tipo); cmd.Parameters.AddWithValue("$importe", m.ImporteMinor);
        cmd.Parameters.AddWithValue("$venta", (object?)m.VentaId ?? DBNull.Value); cmd.Parameters.AddWithValue("$cambio", (object?)m.CambioId ?? DBNull.Value); cmd.Parameters.AddWithValue("$abono", (object?)m.AbonoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op", m.OperacionId); cmd.Parameters.AddWithValue("$vence", (object?)m.Vencimiento ?? DBNull.Value); cmd.Parameters.AddWithValue("$motivo", (object?)m.Motivo ?? DBNull.Value); cmd.Parameters.AddWithValue("$usuario", m.UsuarioId); cmd.Parameters.AddWithValue("$fecha", m.CreadoEn);
        await cmd.ExecuteNonQueryAsync(ct); return id;
    }

    public async Task InsertarAplicacionAsync(ISesionPersistencia sesion, string reductorId, AplicacionCuentaCorriente a, string fecha, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, "INSERT INTO cuenta_corriente_aplicacion(id,movimiento_reductor_id,movimiento_cargo_id,abono_componente_id,importe_minor,saldo_cargo_posterior_minor,creado_en) VALUES($id,$reductor,$cargo,$componente,$importe,$saldo,$fecha);");
        cmd.Parameters.AddWithValue("$id", ids.NuevoId()); cmd.Parameters.AddWithValue("$reductor", reductorId); cmd.Parameters.AddWithValue("$cargo", a.CargoId); cmd.Parameters.AddWithValue("$componente", (object?)a.ComponenteAbonoId ?? DBNull.Value); cmd.Parameters.AddWithValue("$importe", a.ImporteMinor); cmd.Parameters.AddWithValue("$saldo", (object?)a.SaldoCargoPosteriorMinor ?? DBNull.Value); cmd.Parameters.AddWithValue("$fecha", fecha);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ObtenerSiguienteNumeroAbonoAsync(ISesionPersistencia sesion, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, "UPDATE secuencia_abono SET ultimo_numero=ultimo_numero+1 WHERE id=1 RETURNING ultimo_numero;");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<string> InsertarAbonoAsync(ISesionPersistencia sesion, string numero, string clienteId, long importe, long saldoAnterior, string operacionId, string usuarioId, string fecha, CancellationToken ct = default)
    {
        var id = ids.NuevoId(); using var cmd = Crear(sesion, "INSERT INTO cuenta_corriente_abono(id,numero,cliente_id,importe_minor,saldo_anterior_minor,saldo_posterior_minor,operacion_id,usuario_id,creado_en) VALUES($id,$numero,$cliente,$importe,$antes,$despues,$op,$usuario,$fecha);");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$numero", numero); cmd.Parameters.AddWithValue("$cliente", clienteId); cmd.Parameters.AddWithValue("$importe", importe); cmd.Parameters.AddWithValue("$antes", saldoAnterior); cmd.Parameters.AddWithValue("$despues", saldoAnterior-importe); cmd.Parameters.AddWithValue("$op", operacionId); cmd.Parameters.AddWithValue("$usuario", usuarioId); cmd.Parameters.AddWithValue("$fecha", fecha);
        await cmd.ExecuteNonQueryAsync(ct); return id;
    }

    public async Task<string> InsertarComponenteAbonoAsync(ISesionPersistencia sesion, string abonoId, NuevoPago pago, CancellationToken ct = default)
    {
        var id = ids.NuevoId(); using var cmd = Crear(sesion, "INSERT INTO cuenta_corriente_abono_componente(id,abono_id,medio,importe_minor,recibido_minor) VALUES($id,$abono,$medio,$importe,$recibido);");
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$abono", abonoId); cmd.Parameters.AddWithValue("$medio", pago.Medio); cmd.Parameters.AddWithValue("$importe", pago.ImporteMinor); cmd.Parameters.AddWithValue("$recibido", (object?)pago.RecibidoMinor ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct); return id;
    }

    public async Task<DocumentoTicketAbono?> ObtenerTicketAbonoAsync(ISesionPersistencia sesion, string abonoId, CancellationToken ct = default)
    {
        using var cmd = Crear(sesion, "SELECT a.numero,c.nombre_completo,a.creado_en,a.importe_minor,a.saldo_anterior_minor,a.saldo_posterior_minor FROM cuenta_corriente_abono a JOIN cliente c ON c.id=a.cliente_id WHERE a.id=$id;"); cmd.Parameters.AddWithValue("$id", abonoId);
        string numero,nombre,fecha; long importe,saldoAntes,saldoDespues;
        await using (var r = await cmd.ExecuteReaderAsync(ct)) { if (!await r.ReadAsync(ct)) return null; numero=r.GetString(0); nombre=r.GetString(1); fecha=r.GetString(2); importe=r.GetInt64(3); saldoAntes=r.GetInt64(4); saldoDespues=r.GetInt64(5); }
        using var pagosCmd = Crear(sesion, "SELECT medio,importe_minor,recibido_minor FROM cuenta_corriente_abono_componente WHERE abono_id=$id ORDER BY rowid;"); pagosCmd.Parameters.AddWithValue("$id", abonoId);
        var pagos=new List<PagoTicketAbono>(); await using(var r=await pagosCmd.ExecuteReaderAsync(ct)) while(await r.ReadAsync(ct)){long amount=r.GetInt64(1);long? received=r.IsDBNull(2)?null:r.GetInt64(2);pagos.Add(new(r.GetString(0),amount,received,received.HasValue?received.Value-amount:null));}
        using var movCmd=Crear(sesion,"SELECT id,importe_minor FROM cuenta_corriente_movimiento WHERE abono_id=$id AND tipo='abono';");movCmd.Parameters.AddWithValue("$id",abonoId);
        string movementId; await using(var r=await movCmd.ExecuteReaderAsync(ct)){if(!await r.ReadAsync(ct))return null;movementId=r.GetString(0);}
        using var appCmd=Crear(sesion,"SELECT COALESCE(v.numero,'Ajuste de deuda'),SUM(ap.importe_minor),(SELECT z.saldo_cargo_posterior_minor FROM cuenta_corriente_aplicacion z WHERE z.movimiento_reductor_id=$mov AND z.movimiento_cargo_id=cargo.id ORDER BY z.rowid DESC LIMIT 1) FROM cuenta_corriente_aplicacion ap JOIN cuenta_corriente_movimiento cargo ON cargo.id=ap.movimiento_cargo_id LEFT JOIN venta v ON v.id=cargo.venta_id WHERE ap.movimiento_reductor_id=$mov GROUP BY cargo.id ORDER BY cargo.creado_en,cargo.id;");appCmd.Parameters.AddWithValue("$mov",movementId);
        var apps=new List<AplicacionAbonoRecibo>();await using(var r=await appCmd.ExecuteReaderAsync(ct))while(await r.ReadAsync(ct))apps.Add(new(r.GetString(0),r.GetInt64(1),r.IsDBNull(2)?0:r.GetInt64(2)));
        return new(numero,nombre,fecha,importe,saldoAntes,saldoDespues,pagos,apps);
    }

    public async Task<IReadOnlyList<(CargoCuentaCorriente Cargo, string ComponenteId, string Medio, long Importe)>> ListarAplicacionesPagadasVentaAsync(ISesionPersistencia sesion, string ventaId, CancellationToken ct = default)
    {
        using var cmd=Crear(sesion,"SELECT cargo.id,cargo.venta_id,v.numero,cargo.importe_minor,cargo.importe_minor-COALESCE((SELECT SUM(x.importe_minor) FROM cuenta_corriente_aplicacion x WHERE x.movimiento_cargo_id=cargo.id),0),cargo.creado_en,cargo.vencimiento,ac.id,ac.medio,SUM(ap.importe_minor) FROM cuenta_corriente_movimiento cargo JOIN venta v ON v.id=cargo.venta_id JOIN cuenta_corriente_aplicacion ap ON ap.movimiento_cargo_id=cargo.id JOIN cuenta_corriente_abono_componente ac ON ac.id=ap.abono_componente_id WHERE cargo.tipo='cargo_venta' AND cargo.venta_id=$venta GROUP BY cargo.id,ac.id ORDER BY cargo.creado_en,cargo.id,ac.id;");cmd.Parameters.AddWithValue("$venta",ventaId);
        var rows=new List<(CargoCuentaCorriente,string,string,long)>();await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct)){var cargo=new CargoCuentaCorriente(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetInt64(4),r.GetString(5),r.IsDBNull(6)?null:r.GetString(6));rows.Add((cargo,r.GetString(7),r.GetString(8),r.GetInt64(9)));}return rows;
    }

    public async Task<long> SumarReintegrosPorComponenteAsync(ISesionPersistencia sesion,string componenteId,CancellationToken ct=default)
    {using var cmd=Crear(sesion,"SELECT COALESCE(-SUM(importe_minor),0) FROM caja_movimiento WHERE abono_componente_id=$id AND tipo='reintegro';");cmd.Parameters.AddWithValue("$id",componenteId);return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));}

    public async Task<long> SumarReintegrosPorComponenteVentaAsync(ISesionPersistencia sesion,string componenteId,string ventaId,CancellationToken ct=default)
    {using var cmd=Crear(sesion,"SELECT COALESCE(-SUM(importe_minor),0) FROM caja_movimiento WHERE abono_componente_id=$componente AND venta_id=$venta AND tipo='reintegro';");cmd.Parameters.AddWithValue("$componente",componenteId);cmd.Parameters.AddWithValue("$venta",ventaId);return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));}

    public async Task<IReadOnlyList<MovimientoCuentaCorriente>> ListarMovimientosAsync(ISesionPersistencia sesion,string clienteId,int limite,int desplazamiento,CancellationToken ct=default)
    {
        if(limite is <1 or >100||desplazamiento<0)throw new ArgumentOutOfRangeException(nameof(limite));
        using var cmd=Crear(sesion,"WITH libro AS (SELECT m.*,SUM(m.importe_minor) OVER(ORDER BY m.creado_en,m.id ROWS UNBOUNDED PRECEDING) saldo,COALESCE(v.numero,c.numero,a.numero) referencia FROM cuenta_corriente_movimiento m LEFT JOIN venta v ON v.id=m.venta_id LEFT JOIN cambio c ON c.id=m.cambio_id LEFT JOIN cuenta_corriente_abono a ON a.id=m.abono_id WHERE m.cliente_id=$cliente) SELECT id,tipo,importe_minor,creado_en,referencia,vencimiento,motivo,saldo,venta_id,cambio_id,abono_id FROM libro ORDER BY creado_en DESC,id DESC LIMIT $limite OFFSET $offset;");cmd.Parameters.AddWithValue("$cliente",clienteId);cmd.Parameters.AddWithValue("$limite",limite);cmd.Parameters.AddWithValue("$offset",desplazamiento);
        var rows=new List<MovimientoCuentaCorriente>();await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2),r.GetString(3),N(r,4),N(r,5),N(r,6),r.GetInt64(7),N(r,8),N(r,9),N(r,10)));return rows;
    }

    private const string SqlCargos="SELECT m.id,m.venta_id,COALESCE(v.numero,'Ajuste de deuda'),m.importe_minor,m.importe_minor-COALESCE(a.aplicado,0),m.creado_en,m.vencimiento FROM cuenta_corriente_movimiento m LEFT JOIN venta v ON v.id=m.venta_id LEFT JOIN (SELECT movimiento_cargo_id,SUM(importe_minor) aplicado FROM cuenta_corriente_aplicacion GROUP BY movimiento_cargo_id) a ON a.movimiento_cargo_id=m.id";
    private static async Task<IReadOnlyList<CargoCuentaCorriente>> LeerCargos(Microsoft.Data.Sqlite.SqliteCommand cmd,CancellationToken ct){var rows=new List<CargoCuentaCorriente>();await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))rows.Add(new(r.GetString(0),N(r,1),r.GetString(2),r.GetInt64(3),r.GetInt64(4),r.GetString(5),N(r,6)));return rows;}
    private static string? N(Microsoft.Data.Sqlite.SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static Microsoft.Data.Sqlite.SqliteCommand Crear(ISesionPersistencia sesion,string sql){var s=sesion as ISesionSqlite??throw new ArgumentException("La sesión no pertenece a SQLite.");var cmd=s.Conexion.CreateCommand();cmd.CommandTimeout=s.TimeoutSegundos;cmd.CommandText=sql;return cmd;}
}
