namespace CharterTrip.Core.Abstractions;

/// <summary>Injected so tests can control time instead of sleeping.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
