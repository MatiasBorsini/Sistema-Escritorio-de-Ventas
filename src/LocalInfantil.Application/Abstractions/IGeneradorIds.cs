namespace LocalInfantil.Application.Abstractions;

/// <summary>
/// Genera identificadores estables y únicos para registros y operaciones.
/// La versión real usa GUID; las pruebas inyectan un generador secuencial
/// determinista. Consultar nunca genera identificadores nuevos.
/// </summary>
public interface IGeneradorIds
{
    string NuevoId();
}
