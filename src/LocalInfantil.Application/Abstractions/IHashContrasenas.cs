namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Huella de contraseñas con sal aleatoria individual. Nunca se guarda ni
/// se compara texto plano. La huella incluye su versión para permitir
/// actualizar el algoritmo por etapas (ver NecesitaActualizacion).
/// </summary>
public interface IHashContrasenas
{
    string CrearHuella(string contrasena);

    bool Verificar(string contrasena, string? huella);

    bool NecesitaActualizacion(string? huella);
}
