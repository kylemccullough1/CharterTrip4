namespace CharterTrip.Core.Services;

/// <summary>Short, readable, collision-safe-enough ids for hand-editable JSON.</summary>
public static class Ids
{
    public static string New(string prefix) =>
        $"{prefix}-{Guid.NewGuid().ToString("n")[..8]}";
}
