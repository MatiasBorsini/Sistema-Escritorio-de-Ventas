using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Contracts;
using LocalInfantil.Application.Features.Autenticacion;

namespace LocalInfantil.Application.Features.Compras;

public sealed class ConsultasRecepciones
{
    private readonly IRepositorioRecepciones _recepciones;
    private readonly GestorSesiones _sesiones;
    private readonly IFabricaSesionesPersistencia _fabrica;

    public ConsultasRecepciones(IRepositorioRecepciones recepciones, GestorSesiones sesiones, IFabricaSesionesPersistencia fabrica)
    {
        _recepciones = recepciones;
        _sesiones = sesiones;
        _fabrica = fabrica;
    }

    public async Task<RecepcionRegistrada?> ObtenerAsync(string id, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ObtenerAsync(sesion, id, cancellationToken);
    }

    public async Task<IReadOnlyList<RecepcionRegistrada>> ListarAsync(int limite, int desplazamiento, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ListarAsync(sesion, limite, desplazamiento, cancellationToken);
    }

    public async Task<IReadOnlyList<LineaRecepcion>> ListarLineasAsync(string recepcionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ListarLineasAsync(sesion, recepcionId, cancellationToken);
    }

    /// <summary>Historial con agregados en una lectura (peek +1 para HaySiguiente).</summary>
    public async Task<IReadOnlyList<ResumenRecepcion>> BuscarResumenAsync(
        string texto,
        string filtro,
        int limite,
        int desplazamiento,
        string? idSesion,
        DateOnly? desde = null,
        DateOnly? hasta = null,
        CancellationToken cancellationToken = default)
    {
        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            throw new ArgumentException("La fecha Desde no puede ser posterior a la fecha Hasta.");
        if (hasta == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(hasta), "La fecha Hasta no permite calcular el día siguiente.");

        DateTimeOffset? desdeUtc = desde.HasValue ? InicioDiaLocalUtc(desde.Value) : null;
        DateTimeOffset? hastaUtc = hasta.HasValue ? InicioDiaLocalUtc(hasta.Value.AddDays(1)) : null;
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.BuscarResumenAsync(
            sesion,
            texto ?? string.Empty,
            filtro ?? FiltroRecepciones.Todas,
            limite,
            desplazamiento,
            cancellationToken,
            desdeUtc,
            hastaUtc);
    }

    private static DateTimeOffset InicioDiaLocalUtc(DateOnly fecha)
    {
        var inicioLocal = DateTime.SpecifyKind(fecha.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(inicioLocal, TimeZoneInfo.Local), TimeSpan.Zero);
    }

    /// <summary>Líneas con variante resuelta en una lectura (detalle bajo demanda).</summary>
    public async Task<IReadOnlyList<LineaRecepcionDetalle>> ListarLineasDetalleAsync(string recepcionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ListarLineasDetalleAsync(sesion, recepcionId, cancellationToken);
    }

    public async Task<bool> EstaRevertidaAsync(string recepcionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExisteReversionAsync(sesion, recepcionId, cancellationToken);
    }

    public async Task<bool> ExisteOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExistePorOperacionAsync(sesion, operacionId, cancellationToken);
    }

    public async Task<RecepcionRegistrada?> ObtenerPorOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ObtenerPorOperacionAsync(sesion, operacionId, cancellationToken);
    }

    public async Task<bool> ExisteReversionPorOperacionAsync(string operacionId, string? idSesion, CancellationToken cancellationToken = default)
    {
        await using var sesion = await SesionValidadaAsync(idSesion, cancellationToken);
        return await _recepciones.ExisteReversionPorOperacionAsync(sesion, operacionId, cancellationToken);
    }

    private async Task<ISesionPersistencia> SesionValidadaAsync(string? idSesion, CancellationToken cancellationToken)
    {
        if (idSesion is null || _sesiones.Obtener(idSesion) is null)
            throw new SesionInvalidaException();
        return await _fabrica.IniciarLecturaAsync(cancellationToken);
    }
}
