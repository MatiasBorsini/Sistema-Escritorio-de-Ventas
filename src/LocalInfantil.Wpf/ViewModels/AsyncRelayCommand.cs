using System.Windows.Input;

namespace LocalInfantil.Wpf.ViewModels;

/// <summary>
/// Ejecuta una operación asíncrona y evita iniciar una segunda mientras la primera sigue en curso.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private bool _ejecutando;

    public AsyncRelayCommand(Func<Task> executeAsync)
        : this(_ => executeAsync())
    {
    }

    /// <summary>Variante con parámetro (p. ej. ítem seleccionado de un buscador).</summary>
    public AsyncRelayCommand(Func<object?, Task> executeAsync)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_ejecutando;

    public async void Execute(object? parameter)
    {
        if (_ejecutando)
        {
            return;
        }

        _ejecutando = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            await _executeAsync(parameter);
        }
        finally
        {
            _ejecutando = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
