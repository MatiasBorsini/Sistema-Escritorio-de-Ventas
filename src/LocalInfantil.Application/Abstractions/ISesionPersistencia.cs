namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Sesión de trabajo sobre la base: agrupa varias operaciones en una misma
/// conexión y transacción. Confirmar persiste todo junto; descartar (o
/// liberar sin confirmar) revierte todo. No hay reintentos automáticos:
/// ante un fallo, la sesión se descarta y el llamador decide con su
/// identificador de operación si reintenta.
/// </summary>
public interface ISesionPersistencia : IAsyncDisposable
{
    Task ConfirmarAsync(CancellationToken cancellationToken = default);

    Task RevertirAsync(CancellationToken cancellationToken = default);
}
