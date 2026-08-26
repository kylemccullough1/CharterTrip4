namespace CharterTrip.Web.Components.Shared;

/// <summary>What a commit should do, once the value underneath it may have moved.</summary>
public enum EditOutcome
{
    /// <summary>Nothing to do — what was typed is already what it says.</summary>
    Skip,

    /// <summary>Write it.</summary>
    Write,

    /// <summary>Somebody else got here first. Say so and keep the box open.</summary>
    Warn
}

/// <summary>
/// The rule for two people editing one field at once.
///
/// Its own testable thing rather than an <c>if</c> buried in a component, because getting it
/// wrong loses somebody's writing without either of them ever knowing — the failure is silent,
/// so it needs to be checked by something other than noticing.
/// </summary>
public static class EditConflict
{
    /// <param name="opened">What the field said when the box was opened.</param>
    /// <param name="current">What it says now, which is not always the same thing.</param>
    /// <param name="draft">What has been typed, already trimmed.</param>
    public static EditOutcome Decide(string opened, string current, string draft)
    {
        // Agreeing with what is already there is not a write, however it got that way.
        if (draft == current) return EditOutcome.Skip;

        // It moved while the box was open, and the draft disagrees with where it moved to.
        // Warn rather than write: the first commit buys the news, a second one overwrites.
        if (current != opened) return EditOutcome.Warn;

        return EditOutcome.Write;
    }
}
