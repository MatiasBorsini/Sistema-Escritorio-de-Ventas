namespace LocalInfantil.Infrastructure.Persistence.Migrations;

/// <summary>
/// Migración versionada del esquema. Cada versión se aplica una sola vez,
/// en orden creciente, sobre una base vacía o ya migrada. Nunca se reescribe
/// una migración aplicada: los cambios van en una versión nueva.
/// </summary>
/// <param name="Version">Número de versión, único y creciente.</param>
/// <param name="Nombre">Nombre corto y estable de la migración.</param>
/// <param name="Sql">Sentencias DDL/DML de la migración.</param>
public sealed record Migracion(int Version, string Nombre, string Sql);
