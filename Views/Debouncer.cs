namespace MkalInventoryApp.Views;

/// <summary>Waits until typing pauses before running a search, so fast typists do not trigger a query per keystroke.</summary>
public sealed class Debouncer
{
    private CancellationTokenSource? _cts;

    public async Task RunAsync(Func<Task> action, int milliseconds = 250)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try { await Task.Delay(milliseconds, cts.Token); }
        catch (TaskCanceledException) { return; }
        await action();
    }
}
