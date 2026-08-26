using CharterTrip.Web.Components.Shared;

namespace CharterTrip.Tests;

/// <summary>
/// Two people editing one field. Writes are serialised and every change is broadcast, so the
/// only thing that can be lost is an edit typed against a value that has since moved — and it
/// would be lost silently, which is why the rule is checked here rather than by noticing.
/// </summary>
public class EditConflictTests
{
    [Fact]
    public void An_ordinary_edit_is_written()
    {
        Assert.Equal(EditOutcome.Write, EditConflict.Decide("Tulsa", "Tulsa", "OKC"));
    }

    [Fact]
    public void Typing_nothing_new_writes_nothing()
    {
        Assert.Equal(EditOutcome.Skip, EditConflict.Decide("Tulsa", "Tulsa", "Tulsa"));
    }

    /// <summary>The case worth having: somebody else's answer is not overwritten unannounced.</summary>
    [Fact]
    public void An_edit_typed_against_a_value_that_moved_warns_instead_of_writing()
    {
        Assert.Equal(EditOutcome.Warn, EditConflict.Decide("Dairy", "Lactose", "No dairy"));
    }

    /// <summary>
    /// Warned once, the next commit is an informed one. The component re-baselines on the warning,
    /// which is what turns the second attempt into a deliberate overwrite.
    /// </summary>
    [Fact]
    public void The_second_attempt_after_a_warning_goes_through()
    {
        Assert.Equal(EditOutcome.Write, EditConflict.Decide("Lactose", "Lactose", "No dairy"));
    }

    /// <summary>
    /// Landing on the same wording as the other person is agreement, not a collision — there is
    /// nothing to warn about and nothing to write.
    /// </summary>
    [Fact]
    public void Agreeing_with_whoever_got_there_first_is_not_a_conflict()
    {
        Assert.Equal(EditOutcome.Skip, EditConflict.Decide("Dairy", "No dairy", "No dairy"));
    }

    /// <summary>Clearing a field is a real edit, and it collides like any other.</summary>
    [Theory]
    [InlineData("Dairy", "Dairy", "", EditOutcome.Write)]
    [InlineData("Dairy", "Lactose", "", EditOutcome.Warn)]
    [InlineData("Dairy", "", "", EditOutcome.Skip)]
    public void Emptying_a_field_follows_the_same_rule(
        string opened, string current, string draft, EditOutcome expected)
    {
        Assert.Equal(expected, EditConflict.Decide(opened, current, draft));
    }
}
