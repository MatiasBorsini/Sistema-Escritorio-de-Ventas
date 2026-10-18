namespace LocalInfantil.Wpf.Infra;

/// <summary>Áreas de datos que pueden invalidar consultas ya visibles en otras pantallas.</summary>
public enum AreaDatos
{
    Proveedores,
    Catalogo,
    Stock,
    Recepciones,
    Conteos,
    Ventas,
    Caja,
    Cambios,
    Clientes,
    CuentaCorriente,
    MercadoPago,
}

public sealed class CambioDatosConfirmadoEventArgs(object? emisor, AreaDatos origen, IReadOnlySet<AreaDatos> areasAfectadas) : EventArgs
{
    public object? Emisor { get; } = emisor;
    public AreaDatos Origen { get; } = origen;
    public IReadOnlySet<AreaDatos> AreasAfectadas { get; } = areasAfectadas;
}

/// <summary>
/// Notificación pequeña y local al proceso. Solo se publica después de que el
/// caso de uso confirmó su transacción; no ejecuta consultas ni contiene datos de negocio.
/// </summary>
public sealed class NotificadorCambiosDatos
{
    public event EventHandler<CambioDatosConfirmadoEventArgs>? CambioConfirmado;

    public void Publicar(object emisor, AreaDatos origen, params AreaDatos[] areasAfectadas)
    {
        ArgumentNullException.ThrowIfNull(emisor);
        ArgumentNullException.ThrowIfNull(areasAfectadas);
        var args = new CambioDatosConfirmadoEventArgs(emisor, origen, new HashSet<AreaDatos>(areasAfectadas));
        var suscriptores = CambioConfirmado;
        if (suscriptores is null) return;
        foreach (EventHandler<CambioDatosConfirmadoEventArgs> suscriptor in suscriptores.GetInvocationList())
        {
            try { suscriptor(this, args); }
            catch { /* Una actualización de pantalla no revierte ni oculta una escritura confirmada. */ }
        }
    }
}
