using Xunit;

namespace LocalInfantil.Tests.Wpf;

/// <summary>
/// Las pruebas de cierre usan una <see cref="System.Windows.Application"/> real
/// y ventanas modales; se ejecutan aisladas del resto de las clases WPF para no
/// compartir el singleton de Application entre hilos STA distintos.
/// </summary>
[CollectionDefinition("WpfCambiosCierre", DisableParallelization = true)]
public sealed class WpfCambiosCierreCollection
{
}
