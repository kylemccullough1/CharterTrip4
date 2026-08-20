namespace CharterTrip.Web.Services;

public sealed record Toast(Guid Id, string Message, ToastKind Kind);

public enum ToastKind { Info, Good, Bad }

/// <summary>
/// Transient "that worked" messages. Scoped, so each browser tab gets its own — a toast is
/// feedback for the person who clicked, not an announcement to the whole party.
/// </summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Current => _toasts;

    public event Action? Changed;

    public void Show(string message, ToastKind kind = ToastKind.Info)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind);
        _toasts.Add(toast);
        Changed?.Invoke();

        _ = RemoveAfterDelayAsync(toast);
    }

    private async Task RemoveAfterDelayAsync(Toast toast)
    {
        await Task.Delay(TimeSpan.FromSeconds(2.6));
        if (_toasts.Remove(toast)) Changed?.Invoke();
    }
}
