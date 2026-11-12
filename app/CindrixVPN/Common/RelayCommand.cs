using System.Windows.Input;
using CindrixVPN.Services;

namespace CindrixVPN.Common;

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task>? _async;
    private readonly Action? _sync;

    public RelayCommand(Action action) => _sync = action;
    public RelayCommand(Func<Task> action) => _async = action;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            if (_async != null) await _async();
            else _sync?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Command failed: " + ex.Message);
        }
    }
}
