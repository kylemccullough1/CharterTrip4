using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// Moving the evening along.
///
/// No clock decides anything in this game. Every transition is one of the four staff members
/// pressing a button, because the failure mode of a party is not "the host is slow" — it is the app
/// moving on while eleven people are still in the kitchen. The timed phases carry a suggested
/// length (<see cref="MysteryPhaseDurations"/>) that the host's console counts down, and that is
/// all it does: a countdown reaching zero changes nothing.
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
        mystery.Play.PhaseEnteredAt = now;

        switch (phase)
        {
            case MysteryPhase.Presentation:
                mystery.Play.SlideIndex = 0;
                break;

            case MysteryPhase.Introductions:
                mystery.Play.IntroIndex = 0;
                IssueIntroObjective(trip, now);
                break;

            case MysteryPhase.Murder:
                // Stamped so a screen opened at ten o'clock does not replay the cinematic at
                // whoever just walked in.
                mystery.Play.MurderAt ??= now;
                break;

            case MysteryPhase.StudyScene:
                // The study's card is the first clue, and it is handed to everybody: the room is
                // standing at the door when the butler finishes, and the card is the scene itself.
                // Recorded as a scan, so it sits on the Clues tab like any other card.
                GiveTheStudyCard(trip, now);
                break;

            case MysteryPhase.Trial1:
            case MysteryPhase.Trial2:
            case MysteryPhase.Trial3:
                TrialService.OpenTrial(trip, phase, now);
                break;

            case MysteryPhase.Reveal:
                // Settled here rather than on the reveal screen, which was reading an Outcome that
                // nothing ever wrote and headlining the end of the night with an ellipsis. Written
                // down once so every open screen reads the same ending, and recomputed on a
                // re-entry because the host can jump back to a trial and change it.
                OutcomeService.End(trip, now);
                mystery.Play.RevealIndex = 0;
                break;
        }

        ObjectiveBus.PublishForPhase(trip, phase, now);
        return true;
    }

    /// <summary>The card in the one room nobody may enter, given to every guest at once.</summary>
    public static string? StudyClueId(TripData trip)
    {
        var story = trip.Mystery.Story;
        return story.Clues.FirstOrDefault(c => story.Zone(c.ZoneId) is { PlayersAllowed: false })?.Id;
    }

    private static void GiveTheStudyCard(TripData trip, DateTimeOffset now)
    {
        if (StudyClueId(trip) is not { } clueId) return;

        foreach (var guest in trip.Mystery.Story.Guests)
            ScanShareService.RecordClueScan(trip, guest.Id, clueId, now);
    }

    /// <summary>The next phase along, or false at the end.</summary>
    public static bool Next(TripData trip, DateTimeOffset now) =>
        MysteryPhases.Next(trip.Mystery.Phase) is { } next && GoToPhase(trip, next, now);

    /// <summary>
    /// When the current phase's suggested time is up, or null for a phase with no suggestion.
    /// A display value for the console and nothing else.
    /// </summary>
    public static DateTimeOffset? PhaseDeadline(TripData trip) =>
        trip.Mystery.Play.PhaseEnteredAt is { } entered
        && MysteryPhaseDurations.For(trip.Mystery.Phase) is { } length
            ? entered + length
            : null;

    // ------------------------------------------------------------------------------------------
    //  The introductions
    // ------------------------------------------------------------------------------------------

    /// <summary>The template a person is sent when it is their turn to stand up.</summary>
    public const string IntroTemplateId = "intro-speak";

    /// <summary>
    /// Everybody, in the order they stand up: Braun first to set the scene, then the three who
    /// are secretly running the evening, then the guests in the order the story lists them.
    /// </summary>
    public static IReadOnlyList<MysteryCharacter> IntroOrder(TripData trip)
    {
        var characters = trip.Mystery.Story.Characters;

        return characters.Where(c => c.Staff == MysteryStaffRole.Host)
            .Concat(characters.Where(c => c.Staff == MysteryStaffRole.Facilitator))
            .Concat(characters.Where(c => !c.IsStaff))
            .ToList();
    }

    /// <summary>Who is standing up right now, or null once everybody has.</summary>
    public static MysteryCharacter? CurrentIntro(TripData trip)
    {
        var order = IntroOrder(trip);
        var i = trip.Mystery.Play.IntroIndex;
        return i >= 0 && i < order.Count ? order[i] : null;
    }

    public static bool OnLastIntro(TripData trip) =>
        trip.Mystery.Play.IntroIndex >= IntroOrder(trip).Count - 1;

    /// <summary>The next person up, and the nudge to their phone. False when there is nobody left.</summary>
    public static bool NextIntro(TripData trip, DateTimeOffset now)
    {
        var play = trip.Mystery.Play;
        if (play.IntroIndex + 1 >= IntroOrder(trip).Count) return false;

        play.IntroIndex++;
        IssueIntroObjective(trip, now);
        return true;
    }

    public static bool PreviousIntro(TripData trip)
    {
        var play = trip.Mystery.Play;
        if (play.IntroIndex <= 0) return false;

        play.IntroIndex--;
        return true;
    }

    /// <summary>
    /// Tell whoever is up that they are up. Once per person, however many times the host steps
    /// back and forward over them — the phone pops the newest objective, and popping the same one
    /// twice reads as a bug.
    /// </summary>
    private static void IssueIntroObjective(TripData trip, DateTimeOffset now)
    {
        if (CurrentIntro(trip) is not { } who) return;

        var already = trip.Mystery.Play.Objectives.Any(o =>
            o.TemplateId == IntroTemplateId && o.CharacterIds.Contains(who.Id));

        if (!already) ObjectiveBus.SendTemplateTo(trip, IntroTemplateId, who.Id, MysteryPhase.Introductions, now);
    }

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
    //  Walking the ending
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The reveal is a deck too: the prose, then the winners one card at a time, then the ending.
    ///
    /// Stepped by the host rather than timed. Faces on a timer is either too fast for the person
    /// being read out or too slow for the room, and it is the last thing that happens all night —
    /// it should end when the laughing stops.
    /// </summary>
    public static int RevealSteps(TripData trip) =>
        RevealOpening(trip) + OutcomeService.Reveal(trip).Count + 1;

    /// <summary>
    /// How many cards the story takes: one per written paragraph, so each fits a television
    /// without scrolling. At least one, so a story nobody has written still has an opening.
    /// </summary>
    public static int RevealOpening(TripData trip) =>
        Math.Max(1, MysteryText.WrittenOnly(trip.Mystery.Story.Beats.RevealParagraphs).Count());

    /// <summary>The paragraph of the story this step shows, or null once the story is told.</summary>
    public static string? RevealParagraph(TripData trip)
    {
        var paragraphs = MysteryText.WrittenOnly(trip.Mystery.Story.Beats.RevealParagraphs).ToList();
        var i = trip.Mystery.Play.RevealIndex;
        return i >= 0 && i < paragraphs.Count ? paragraphs[i] : null;
    }

    /// <summary>Whether the ending is still on the story, before any winner.</summary>
    public static bool OnRevealOpening(TripData trip) =>
        trip.Mystery.Play.RevealIndex < RevealOpening(trip);

    /// <summary>Which winner the ending is on, or null on the story and the closing card.</summary>
    public static OutcomeService.RevealCard? RevealSubject(TripData trip)
    {
        var cards = OutcomeService.Reveal(trip);
        var i = trip.Mystery.Play.RevealIndex - RevealOpening(trip);
        return i >= 0 && i < cards.Count ? cards[i] : null;
    }

    /// <summary>The ending, and the way out.</summary>
    public static bool OnRevealFinale(TripData trip) =>
        trip.Mystery.Play.RevealIndex >= RevealSteps(trip) - 1;

    public static bool NextReveal(TripData trip)
    {
        var play = trip.Mystery.Play;
        if (play.RevealIndex + 1 >= RevealSteps(trip)) return false;

        play.RevealIndex++;
        return true;
    }

    public static bool PreviousReveal(TripData trip)
    {
        var play = trip.Mystery.Play;
        if (play.RevealIndex <= 0) return false;

        // Clamped first: the deck can get shorter under a saved game — it walked every guest
        // once, and walks only the winners now — and an index past the end has to step back to
        // the last real card rather than through a run of nothing.
        play.RevealIndex = Math.Min(play.RevealIndex, RevealSteps(trip) - 1) - 1;
        return true;
    }

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
    /// Recorded from the first scan, shown from the deliberation after the first trial, where it
    /// becomes how the detectives work out which cards were tampered with. Public movement during
    /// the investigation would make every alibi checkable and leave nothing to lie about.
    /// </summary>
    public static bool TrailVisible(TripData trip) =>
        MysteryPhases.TrailVisible(trip.Mystery.Phase);

    /// <summary>Whether an ability has come online.</summary>
    public static bool IsUnlocked(TripData trip, MysteryAbility ability) =>
        MysteryPhases.AtOrAfter(trip.Mystery.Phase, ability.Unlock);
}
