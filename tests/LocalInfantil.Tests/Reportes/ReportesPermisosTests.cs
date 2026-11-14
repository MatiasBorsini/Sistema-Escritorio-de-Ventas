using LocalInfantil.Application.Abstractions;
using LocalInfantil.Application.Features.Autenticacion;
using LocalInfantil.Application.Features.Reportes;

namespace LocalInfantil.Tests.Reportes;

/// <summary>
/// Permisos reportes.*: Dueña todo, Empleada solo inventario. Backend real,
/// no solo pestaña oculta. Deny-by-default intacto.
/// </summary>
public sealed class ReportesPermisosTests
{
    [Fact]
    public async Task Duena_ve_todo_y_empleada_solo_inventario()
    {
        using var e = await EntornoReportes.CrearAsync();
        var (d, h) = (DateTimeOffset.MinValue.ToString("o"), DateTimeOffset.MaxValue.ToString("o"));

        // Dueña: todo permitido.
        Assert.NotNull(await e.Reportes.ResumenVentasAsync(d, h, e.Sesion));
        Assert.NotNull(await e.Reportes.HistorialCajaAsync(d, h, 20, 0, e.Sesion));
        Assert.NotNull(await e.Reportes.StockActualAsync(string.Empty, null, false, null, 20, 0, e.Sesion));

        // Empleada por matriz: ventas y caja denegados, inventario permitido.
        var empleada = await e.SesionEmpleadaAsync();
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Reportes.ResumenVentasAsync(d, h, empleada));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Reportes.VentasPorDiaAsync(d, h, "+0 hours", empleada));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Reportes.DetalleVentasAsync(d, h, 20, 0, empleada));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Reportes.DesgloseMediosAsync(d, h, empleada));
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Reportes.HistorialCajaAsync(d, h, 20, 0, empleada));
        Assert.NotNull(await e.Reportes.StockActualAsync(string.Empty, null, false, null, 20, 0, empleada));
        Assert.NotNull(await e.Reportes.RankingVentasAsync(d, h, 20, 0, empleada));
        Assert.NotNull(await e.Reportes.SinVentasAsync(d, 20, 0, empleada));
    }

    [Fact]
    public async Task Matriz_inicial_duena_todo_empleada_solo_inventario()
    {
        var matriz = MatrizPermisos.ConValoresConfirmados();
        foreach (var permiso in PermisosReportes.Todos)
        {
            Assert.True(matriz.Permite(RolesLocal.Duena, permiso));
        }

        Assert.True(matriz.Permite(RolesLocal.Empleada, PermisosReportes.Inventario));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosReportes.Ventas));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosReportes.Caja));
        Assert.False(matriz.Permite(RolesLocal.Empleada, PermisosReportes.Costos));
    }
}
