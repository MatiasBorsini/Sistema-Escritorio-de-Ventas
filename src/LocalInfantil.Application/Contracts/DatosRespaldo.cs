namespace LocalInfantil.Application.Contracts;

public sealed record RespaldoVerificado(string Ruta, IReadOnlyList<int> Versiones, long TamanoBytes, string CreadoEn);

public sealed record VerificacionRespaldo(IReadOnlyList<int> Versiones, long TamanoBytes);
