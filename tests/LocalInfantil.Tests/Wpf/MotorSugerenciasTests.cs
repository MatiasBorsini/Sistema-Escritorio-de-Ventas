using LocalInfantil.Wpf.Infra;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// MotorSugerencias: debounce, cancelación, anti-stale, teclado y limpieza.
/// Sin UI ni timers reales (espera manual inyectada).
/// </summary>
public sealed class MotorSugerenciasTests
{
    private sealed class EsperaManual
    {
        public readonly List<TaskCompletionSource<bool>> Pendientes = new();
        public Task Esperar(TimeSpan _1, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);
            lock (Pendientes) Pendientes.Add(tcs);
            return tcs.Task;
        }

        public void Liberar(int indice)
        {
            TaskCompletionSource<bool> tcs;
            lock (Pendientes) tcs = Pendientes[indice];
            tcs.TrySetResult(true);
        }
    }

    [Fact]
    public async Task Debounce_tres_teclas_rapidas_una_sola_consulta()
    {
        var espera = new EsperaManual();
        var consultas = new List<string>();
        var motor = new MotorSugerencias<string>(async (t, ct) =>
        {
            await Task.Yield();
            lock (consultas) consultas.Add(t);
            return (IReadOnlyList<string>)new[] { t + "-1" };
        }, espera: espera.Esperar);

        var t1 = motor.TextoCambiadoAsync("re");
        var t2 = motor.TextoCambiadoAsync("rem");
        var t3 = motor.TextoCambiadoAsync("reme");
        Assert.Equal(3, espera.Pendientes.Count);
        espera.Liberar(2);
        await Task.WhenAll(t1, t2, t3);

        Assert.Single(consultas);
        Assert.Equal("reme", consultas[0]);
        Assert.Equal(new[] { "reme-1" }, motor.Sugerencias);
    }

    [Fact]
    public async Task Cancelar_anterior_al_seguir_escribiendo()
    {
        var espera = new EsperaManual();
        var puertaA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var consultas = new List<string>();
        var motor = new MotorSugerencias<string>(async (t, ct) =>
        {
            lock (consultas) consultas.Add(t);
            if (t == "rem")
            {
                using (ct.Register(() => puertaA.TrySetCanceled()))
                    await puertaA.Task;
            }
            ct.ThrowIfCancellationRequested();
            return (IReadOnlyList<string>)new[] { t + "-ok" };
        }, espera: espera.Esperar);

        var t1 = motor.TextoCambiadoAsync("rem");
        espera.Liberar(0);
        await Task.Delay(50);
        var t2 = motor.TextoCambiadoAsync("reme");
        espera.Liberar(1);
        await Task.WhenAll(t1, t2);

        Assert.Equal(2, consultas.Count);
        Assert.Equal(new[] { "reme-ok" }, motor.Sugerencias);
        Assert.Null(motor.Error);
    }

    [Fact]
    public async Task Respuesta_vieja_no_pisa_resultado_nuevo()
    {
        var espera = new EsperaManual();
        var puertaA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var motor = new MotorSugerencias<string>(async (t, ct) =>
        {
            if (t == "rem")
            {
                // Proveedor que ignora la cancelación y responde tarde.
                await puertaA.Task;
                return (IReadOnlyList<string>)new[] { "viejo" };
            }

            await Task.Yield();
            return (IReadOnlyList<string>)new[] { "nuevo" };
        }, espera: espera.Esperar);

        var t1 = motor.TextoCambiadoAsync("rem");
        espera.Liberar(0);
        await Task.Delay(50);
        var t2 = motor.TextoCambiadoAsync("reme");
        espera.Liberar(1);
        await t2;
        puertaA.TrySetResult(true);
        await t1;

        Assert.Equal(new[] { "nuevo" }, motor.Sugerencias);
    }

    [Fact]
    public async Task Flechas_mueven_activa_con_clamp_y_enter_confirma()
    {
        var motor = new MotorSugerencias<string>((_, _) =>
            Task.FromResult((IReadOnlyList<string>)new[] { "a", "b", "c" }),
            debounce: TimeSpan.Zero);
        await motor.TextoCambiadoAsync("ab");
        Assert.Equal(0, motor.IndiceActivo);

        motor.MoverSeleccion(1);
        Assert.Equal(1, motor.IndiceActivo);
        motor.MoverSeleccion(99);
        Assert.Equal(2, motor.IndiceActivo);
        motor.MoverSeleccion(-99);
        Assert.Equal(0, motor.IndiceActivo);
        motor.MoverSeleccion(2);
        Assert.Equal("c", motor.SugerenciaActiva);

        var elegida = motor.ConfirmarSeleccion();
        Assert.Equal("c", elegida);
        Assert.False(motor.Abierto);
    }

    [Fact]
    public async Task Esc_cierra_sin_limpiar_texto()
    {
        var motor = new MotorSugerencias<string>((_, _) =>
            Task.FromResult((IReadOnlyList<string>)new[] { "a" }),
            debounce: TimeSpan.Zero);
        await motor.TextoCambiadoAsync("ab");
        Assert.True(motor.Abierto);
        motor.Cerrar();
        Assert.False(motor.Abierto);
        Assert.Equal("ab", motor.Texto);
        Assert.Null(motor.ConfirmarSeleccion());
    }

    [Fact]
    public async Task Texto_vacio_o_corto_limpia_y_no_consulta()
    {
        var consultas = 0;
        var motor = new MotorSugerencias<string>((_, _) =>
        {
            consultas++;
            return Task.FromResult((IReadOnlyList<string>)new[] { "a" });
        }, debounce: TimeSpan.Zero);
        await motor.TextoCambiadoAsync("abc");
        Assert.True(motor.Abierto);
        await motor.TextoCambiadoAsync("");
        Assert.False(motor.Abierto);
        Assert.Empty(motor.Sugerencias);
        Assert.Equal(1, consultas);
        await motor.TextoCambiadoAsync("x");
        Assert.False(motor.Abierto);
        Assert.Equal(1, consultas);
    }

    [Fact]
    public async Task Error_humano_sin_romper_y_maximo_acotado()
    {
        var motor = new MotorSugerencias<string>((_, _) =>
            Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("db caída")),
            debounce: TimeSpan.Zero, maximo: 10);
        await motor.TextoCambiadoAsync("abc");
        Assert.NotNull(motor.Error);
        Assert.True(motor.Abierto);
        Assert.Empty(motor.Sugerencias);
    }

    [Fact]
    public async Task Maximo_recorta_resultados()
    {
        var motor = new MotorSugerencias<string>((_, _) =>
            Task.FromResult((IReadOnlyList<string>)new[] { "1", "2", "3", "4" }),
            debounce: TimeSpan.Zero, maximo: 2);
        await motor.TextoCambiadoAsync("ab");
        Assert.Equal(2, motor.Sugerencias.Count);
    }
}
