using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Moving the evening along.
///
/// There are no clocks anywhere in this game. Every transition is one of the four staff members
/// pressing a button, because the failure mode of a party is not "the host is slow" — it is the app
/// moving on while eleven people are still in the kitchen.
///
/// The ordinary next button and the skip-anywhere strip call the same method on purpose, so a phase
/// arrived at by walking and a phase arrived at by jumping produce identical state. That is what
/// makes "something broke, drive it by hand" survivable.
/// </summary>
public static class PhaseService
{
    /// <summary>
    /// Put the evening in a phase and do whatever entering it implies.
    ///
    /// Idempotent: asking for the phase it is already in changes nothing, so a double-tap on a
    /// phone with a slow connection cannot open two trials or fire the murder twice.
    /// </summary>
    public static bool GoToPhase(TripData trip, MysteryPhase phase, DateTimeOffset now)
    {
        var mystery = trip.Mystery;
        if (mystery.Phase == phase) return false;

        mystery.Phase = phase;

        switch (phase)
        {
            case MysteryPhase.Presentation:
                mystery.Play.SlideIndex = 0;
                break;

            case MysteryPhase.Murder:
                // Stamped so a screen opened at ten o'clock does not replay the cinematic at
                // whoever just walked in.
                mystery.Play.MurderAt ??= now;
                break;

            case MysteryPhase.Trial1:
            case MysteryPhase.Trial2:
            case MysteryPhase.Trial3:
                TrialService.OpenTrial(trip, phase, now);
                break;
        }

        ObjectiveBus.PublishForPhase(trip, phase, now);
        return true;
    }

    /// <summary>The next phase along, or false at the end.</summary>
    public static bool Next(TripData trip, DateTimeOffset now) =>
        MysteryPhases.Next(trip.Mystery.Phase) is { } next && GoToPhase(trip, next, now);

    /// <summary>Advance the deck. Returns false on the last slide, which is what enables Begin.</summary>
    public static bool NextSlide(TripData trip)
    {
        var play = trip.Mystery.Play;
        if (play.SlideIndex + 1 >= trip.Mystery.Story.Slides.Count) return false;

        play.SlideIndex++;
        return true;
    }

    public static bool PreviousSlide(TripData trip)
    {
        var play = trip.Mystery.Play;
        if (play.SlideIndex <= 0) return false;

        play.SlideIndex--;
        return true;
    }

    public static MysterySlide? CurrentSlide(TripData trip)
    {
        var slides = trip.Mystery.Story.Slides;
        var i = trip.Mystery.Play.SlideIndex;
        return i >= 0 && i < slides.Count ? slides[i] : null;
    }

    public static bool OnLastSlide(TripData trip) =>
        trip.Mystery.Play.SlideIndex >= trip.Mystery.Story.Slides.Count - 1;

    // ------------------------------------------------------------------------------------------
    //  The gates
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether this player knows what they are yet.
    ///
    /// The single unrecoverable bug in this build is this being true one phase early: killers read
    /// their briefings during the party, and the evening is over with no way back. Every surface
    /// asks here rather than comparing phases itself.
    /// </summary>
    public static bool RolesRevealed(TripData trip) =>
        MysteryPhases.RolesRevealed(trip.Mystery.Phase);

    /// <summary>
    /// Whether the map shows who scanned which clue.
    ///
    /// Recorded from the first scan, shown from the first discussion round, where it becomes how
    /// the detectives work out which cards were tampered with. Public movement during Investigation
    /// would make every alibi checkable and leave nothing to lie about.
    /// </summary>
    public static bool TrailVisible(TripData trip) =>
        MysteryPhases.TrailVisible(trip.Mystery.Phase);

    /// <summary>Whether an ability has come online.</summary>
    public static bool IsUnlocked(TripData trip, MysteryAbility ability) =>
        MysteryPhases.AtOrAfter(trip.Mystery.Phase, ability.Unlock);
}
