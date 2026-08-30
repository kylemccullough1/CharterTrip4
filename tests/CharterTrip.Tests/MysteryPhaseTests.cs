using CharterTrip.Core.Models;

namespace CharterTrip.Tests;

/// <summary>
/// The phase order, and the two gates hanging off it.
///
/// This file exists because of one failure mode. Roles drop at the study; if that gate reads true
/// one phase early, three people read "you killed him" while standing in the Grand Hall holding a
/// drink, and there is no way to take it back. Everything here is guarding that.
/// </summary>
public class MysteryPhaseTests
{
    /// <summary>
    /// The enum's declaration order and the game's order agree today, and nothing enforces it. So
    /// Order is written out separately and this checks the two have not drifted — a phase left out
    /// would throw on first use, and a phase silently in the wrong place would move every gate
    /// after it.
    /// </summary>
    [Fact]
    public void Every_phase_appears_in_the_order_exactly_once()
    {
        var declared = Enum.GetValues<MysteryPhase>();

        Assert.Equal(declared.Length, MysteryPhases.Order.Count);
        Assert.Equal(declared.Length, MysteryPhases.Order.Distinct().Count());

        foreach (var phase in declared)
            Assert.Contains(phase, MysteryPhases.Order);
    }

    [Fact]
    public void The_evening_starts_in_the_lobby_and_ends_at_the_reveal()
    {
        Assert.Equal(MysteryPhase.Lobby, MysteryPhases.Order[0]);
        Assert.Equal(MysteryPhase.Reveal, MysteryPhases.Order[^1]);
        Assert.Null(MysteryPhases.Next(MysteryPhase.Reveal));
    }

    [Fact]
    public void Next_walks_the_whole_evening_without_skipping_anything()
    {
        var walked = new List<MysteryPhase> { MysteryPhase.Lobby };

        while (MysteryPhases.Next(walked[^1]) is { } next)
            walked.Add(next);

        Assert.Equal(MysteryPhases.Order, walked);
    }

    [Fact]
    public void AtOrAfter_includes_the_phase_itself()
    {
        Assert.True(MysteryPhases.AtOrAfter(MysteryPhase.StudyScene, MysteryPhase.StudyScene));
        Assert.True(MysteryPhases.AtOrAfter(MysteryPhase.Reveal, MysteryPhase.StudyScene));
        Assert.False(MysteryPhases.AtOrAfter(MysteryPhase.Introductions, MysteryPhase.StudyScene));
    }

    /// <summary>
    /// The introductions happen before anybody is anything. A killer who knows while standing up
    /// to say who they are plays it completely differently, and everybody can tell.
    /// </summary>
    [Theory]
    [InlineData(MysteryPhase.Lobby)]
    [InlineData(MysteryPhase.Assembling)]
    [InlineData(MysteryPhase.Welcome)]
    [InlineData(MysteryPhase.Presentation)]
    [InlineData(MysteryPhase.Introductions)]
    [InlineData(MysteryPhase.Murder)]
    public void Nobody_knows_what_they_are_until_the_study(MysteryPhase phase) =>
        Assert.False(MysteryPhases.RolesRevealed(phase));

    [Theory]
    [InlineData(MysteryPhase.StudyScene)]
    [InlineData(MysteryPhase.Investigation)]
    [InlineData(MysteryPhase.Trial1)]
    [InlineData(MysteryPhase.Reveal)]
    public void From_the_study_onward_everybody_does(MysteryPhase phase) =>
        Assert.True(MysteryPhases.RolesRevealed(phase));

    /// <summary>
    /// Withholding the trail is the mechanic, not an oversight. Shown during the investigation or
    /// the accusation round it makes every alibi checkable, and there is nothing left to lie about.
    /// </summary>
    [Theory]
    [InlineData(MysteryPhase.Introductions)]
    [InlineData(MysteryPhase.StudyScene)]
    [InlineData(MysteryPhase.Investigation)]
    [InlineData(MysteryPhase.Discussion1)]
    [InlineData(MysteryPhase.Trial1)]
    public void The_scan_trail_stays_hidden_through_the_first_trial(MysteryPhase phase) =>
        Assert.False(MysteryPhases.TrailVisible(phase));

    [Theory]
    [InlineData(MysteryPhase.Discussion2)]
    [InlineData(MysteryPhase.Trial2)]
    [InlineData(MysteryPhase.Discussion3)]
    [InlineData(MysteryPhase.Reveal)]
    public void And_opens_as_the_detectives_tool(MysteryPhase phase) =>
        Assert.True(MysteryPhases.TrailVisible(phase));

    /// <summary>The trail is no use before there are roles to spend it on.</summary>
    [Fact]
    public void Roles_land_before_the_trail_does()
    {
        Assert.True(MysteryPhases.AtOrAfter(MysteryPhase.Discussion2, MysteryPhase.StudyScene));
        Assert.False(MysteryPhases.TrailVisible(MysteryPhase.StudyScene));
    }

    [Fact]
    public void There_are_three_trials_and_nothing_else_counts_as_one()
    {
        Assert.Equal(3, MysteryPhases.Order.Count(MysteryPhases.IsTrial));
        Assert.False(MysteryPhases.IsTrial(MysteryPhase.Discussion1));
        Assert.False(MysteryPhases.IsTrial(MysteryPhase.Discussion3));
        Assert.False(MysteryPhases.IsTrial(MysteryPhase.Reveal));
    }

    /// <summary>
    /// The room talks before it votes, every time: an accusation round before the first trial,
    /// a deliberation before each of the others.
    /// </summary>
    [Fact]
    public void Discussions_alternate_with_trials()
    {
        static int At(MysteryPhase p) => MysteryPhases.IndexOf(p);
        Assert.True(At(MysteryPhase.Discussion1) < At(MysteryPhase.Trial1));
        Assert.True(At(MysteryPhase.Trial1) < At(MysteryPhase.Discussion2));
        Assert.True(At(MysteryPhase.Discussion2) < At(MysteryPhase.Trial2));
        Assert.True(At(MysteryPhase.Trial2) < At(MysteryPhase.Discussion3));
        Assert.True(At(MysteryPhase.Discussion3) < At(MysteryPhase.Trial3));
    }

    /// <summary>Four phases have a suggested length; nothing else is on any clock at all.</summary>
    [Fact]
    public void Timed_phases_have_a_suggested_length()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), MysteryPhaseDurations.For(MysteryPhase.Investigation));
        Assert.Equal(TimeSpan.FromMinutes(5), MysteryPhaseDurations.For(MysteryPhase.Discussion1));
        Assert.Equal(TimeSpan.FromMinutes(10), MysteryPhaseDurations.For(MysteryPhase.Discussion2));
        Assert.Equal(TimeSpan.FromMinutes(10), MysteryPhaseDurations.For(MysteryPhase.Discussion3));

        foreach (var phase in MysteryPhases.Order.Where(p => p is not (MysteryPhase.Investigation
                     or MysteryPhase.Discussion1 or MysteryPhase.Discussion2 or MysteryPhase.Discussion3)))
            Assert.Null(MysteryPhaseDurations.For(phase));
    }
}
