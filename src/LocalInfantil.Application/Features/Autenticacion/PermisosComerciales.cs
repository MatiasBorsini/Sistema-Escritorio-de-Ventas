namespace LocalInfantil.Application.Features.Autenticacion;

/// <summary>
/// Únicos identificadores de permiso confirmados (PLAN 0.1). Todo lo demás
/// se deniega por defecto; no existen privilegios sobre descuentos, ajustes,
/// fiados, anulaciones, reportes ni costos. No habilita los reintegros:
/// solo nombra sus permisos para probarlos.
/// </summary>
public static class PermisosComerciales
{
    /// <summary>Dueña y empleada pueden autorizar reintegros (medio habitual: efectivo).</summary>
    public const string ReintegroAutorizar = "reintegro.autorizar";

    /// <summary>Solo la dueña decide el medio excepcional de reintegro.</summary>
    public const string ReintegroMedioExcepcional = "reintegro.medio_excepcional";
}
