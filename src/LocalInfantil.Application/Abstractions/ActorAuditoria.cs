namespace LocalInfantil.Application.Abstractions;

/// <summary>Clases de actor del registro de auditoría.</summary>
public static class ActorAuditoria
{
    /// <summary>Acción realizada por una persona conocida (exige actor_id).</summary>
    public const string Usuario = "usuario";

    /// <summary>Acción interna del sistema, sin persona atribuible.</summary>
    public const string Sistema = "sistema";
}
