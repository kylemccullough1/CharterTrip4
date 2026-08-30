using System.Text;
using CharterTrip.Core.Abstractions;
using CharterTrip.Core.Models;
using CharterTrip.Core.Mystery;
using CharterTrip.Core.Services;
using CharterTrip.Infrastructure.Mystery;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

/// <summary>
/// The reshaped evening: conversations, the blame, the detectives' answers, the introductions,
/// the clock on the console, and the migration that brings an older file into it.
/// </summary>
public class MysteryFlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

    private static TripData In(MysteryPhase phase)
    {
        var trip = new TripData();
        StoryLoader.SeedInto(trip);
        CastingService.OpenDoors(trip, new Random(1));
        trip.Mystery.Phase = phase;
        return trip;
    }

    /// <summary>Three questions on a character, written for the test rather than read from the story.</summary>
    private static void GiveQuestions(TripData trip, string id, string? coverAlibi = null)
    {
        var c = trip.Mystery.Story.Character(id)!;
        c.Questions =
        [
            new() { Id = "alibi", Importance = MysteryQuestionImportance.Alibi, Prompt = $"Where were you, {c.Name}?", Answer = $"{id}: in my room.", CoverAnswer = coverAlibi },
            new() { Id = "q2", Importance = MysteryQuestionImportance.Important, Prompt = "What did you see?", Answer = $"{id}: something.", CoverAnswer = coverAlibi is null ? null : "I saw {target} on the side path." },
            new() { Id = "q3", Importance = MysteryQuestionImportance.Useless, Prompt = "Nice shoes?", Answer = $"{id}: thank you." }
        ];
    }

    // ------------------------------------------------------------------------------------------
    //  Conversations
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_scan_before_the_murder_is_only_a_meeting()
    {
        var trip = In(MysteryPhase.Introductions);

        Assert.Null(InteractionService.Start(trip, "wilhelm", "imogen", At(0)));
        Assert.True(ScanShareService.HasMet(trip, "wilhelm", "imogen"));
        Assert.Empty(trip.Mystery.Play.Interactions);
    }

    [Fact]
    public void The_scanner_asks_first_and_they_take_turns_until_six()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "wilhelm");
        GiveQuestions(trip, "imogen");

        var session = InteractionService.Start(trip, "wilhelm", "imogen", At(0))!;
        Assert.Equal("wilhelm", InteractionService.NextAsker(trip, session));

        // Out of turn is refused; the wrong person's question is refused.
        Assert.Null(InteractionService.Ask(trip, session.Id, "imogen", "alibi", At(1)));

        var first = InteractionService.Ask(trip, session.Id, "wilhelm", "alibi", At(1))!;
        Assert.Equal("imogen: in my room.", first.Answer);
        Assert.Equal("imogen", InteractionService.NextAsker(trip, session));

        // Asked once, gone from the list.
        Assert.NotNull(InteractionService.Ask(trip, session.Id, "imogen", "alibi", At(2)));
        Assert.Null(InteractionService.Ask(trip, session.Id, "wilhelm", "alibi", At(3)));

        Assert.NotNull(InteractionService.Ask(trip, session.Id, "wilhelm", "q2", At(3)));
        Assert.NotNull(InteractionService.Ask(trip, session.Id, "imogen", "q2", At(4)));
        Assert.NotNull(InteractionService.Ask(trip, session.Id, "wilhelm", "q3", At(5)));
        Assert.True(session.IsOpen);
        Assert.NotNull(InteractionService.Ask(trip, session.Id, "imogen", "q3", At(6)));

        Assert.False(session.IsOpen);
        Assert.Equal(6, session.Exchanges.Count);
        Assert.Null(InteractionService.NextAsker(trip, session));
        Assert.Null(InteractionService.OpenFor(trip, "wilhelm"));

        // Over, but not gone: it stays on both phones until each side puts it away.
        Assert.Same(session, InteractionService.ShowingFor(trip, "wilhelm"));
        Assert.Same(session, InteractionService.ShowingFor(trip, "imogen"));

        Assert.True(InteractionService.Dismiss(trip, session.Id, "wilhelm"));
        Assert.Null(InteractionService.ShowingFor(trip, "wilhelm"));
        Assert.Same(session, InteractionService.ShowingFor(trip, "imogen"));

        // Closing twice is nothing; closing somebody else's is nothing.
        Assert.False(InteractionService.Dismiss(trip, session.Id, "wilhelm"));
        Assert.False(InteractionService.Dismiss(trip, session.Id, "yousef"));
    }

    [Fact]
    public void A_new_conversation_puts_the_last_one_away()
    {
        var trip = In(MysteryPhase.Investigation);
        foreach (var id in new[] { "wilhelm", "imogen", "yousef" }) GiveQuestions(trip, id);

        var first = InteractionService.Start(trip, "wilhelm", "imogen", At(0))!;
        Assert.True(InteractionService.Abandon(trip, first.Id, At(1), "imogen"));

        // Imogen walked away and has nothing to read; Wilhelm still has the ended one up.
        Assert.Null(InteractionService.ShowingFor(trip, "imogen"));
        Assert.Same(first, InteractionService.ShowingFor(trip, "wilhelm"));

        var second = InteractionService.Start(trip, "yousef", "wilhelm", At(2))!;
        Assert.Same(second, InteractionService.ShowingFor(trip, "wilhelm"));
        Assert.Contains("wilhelm", first.ClosedBy);
    }

    [Fact]
    public void Starting_again_hands_back_the_same_conversation_from_either_side()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "wilhelm");
        GiveQuestions(trip, "imogen");

        var a = InteractionService.Start(trip, "wilhelm", "imogen", At(0))!;
        var b = InteractionService.Start(trip, "imogen", "wilhelm", At(1))!;

        Assert.Same(a, b);
        Assert.Single(trip.Mystery.Play.Interactions);
    }

    [Fact]
    public void One_conversation_at_a_time_and_leaving_frees_both()
    {
        var trip = In(MysteryPhase.Investigation);
        foreach (var id in new[] { "wilhelm", "imogen", "yousef" }) GiveQuestions(trip, id);

        var open = InteractionService.Start(trip, "wilhelm", "imogen", At(0))!;
        Assert.Null(InteractionService.Start(trip, "yousef", "imogen", At(1)));
        Assert.Null(InteractionService.Start(trip, "wilhelm", "yousef", At(1)));

        Assert.True(InteractionService.Abandon(trip, open.Id, At(2)));
        Assert.Null(InteractionService.OpenFor(trip, "imogen"));
        Assert.NotNull(InteractionService.Start(trip, "yousef", "imogen", At(3)));
    }

    [Fact]
    public void Staff_do_not_have_conversations()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "wilhelm");

        Assert.Null(InteractionService.Start(trip, "wilhelm", "leo", At(0)));
        Assert.Null(InteractionService.Start(trip, "braun", "wilhelm", At(0)));
        Assert.True(ScanShareService.HasMet(trip, "wilhelm", "leo"));
    }

    [Fact]
    public void Stars_are_kept_by_each_side_separately()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "wilhelm");
        GiveQuestions(trip, "imogen");

        var session = InteractionService.Start(trip, "wilhelm", "imogen", At(0))!;
        InteractionService.Ask(trip, session.Id, "wilhelm", "alibi", At(1));

        Assert.True(InteractionService.ToggleStar(trip, session.Id, 0, "wilhelm"));
        Assert.Equal(["wilhelm"], session.Exchanges[0].StarredBy);

        Assert.True(InteractionService.ToggleStar(trip, session.Id, 0, "wilhelm"));
        Assert.Empty(session.Exchanges[0].StarredBy);

        Assert.False(InteractionService.ToggleStar(trip, session.Id, 0, "yousef"));
        Assert.False(InteractionService.ToggleStar(trip, session.Id, 5, "imogen"));
    }

    /// <summary>
    /// The mechanic. A killer's alibi is the plain one until their tamper fires, the cover story
    /// with the framed guest's name after — and a transcript taken before is not rewritten.
    /// </summary>
    [Fact]
    public void A_killers_answers_turn_into_the_cover_story_when_the_tamper_fires()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "wilhelm");
        GiveQuestions(trip, "imogen");
        GiveQuestions(trip, "carla", coverAlibi: "I was on the path — and so was {target}.");
        GiveQuestions(trip, "solomon", coverAlibi: "At the bar. {target} left it at twenty to ten.");

        var early = InteractionService.Start(trip, "wilhelm", "carla", At(0))!;
        var before = InteractionService.Ask(trip, early.Id, "wilhelm", "alibi", At(1))!;
        Assert.Equal("carla: in my room.", before.Answer);
        InteractionService.Abandon(trip, early.Id, At(2));

        // One of the three hands plants on a card, pointing at Imogen.
        var clue = trip.Mystery.Story.Clues[1].Id;
        var use = AbilityService.TryFire(trip, "solomon", "evidence_hand", At(5), "plant", "imogen", clue);
        Assert.NotNull(use);
        ScanShareService.Tamper(trip, clue, "plant", "solomon", "imogen", At(5));

        // All three hands' stories move together, and only the answers that matter.
        Assert.Equal("imogen", InteractionService.CoverTarget(trip, "carla"));
        Assert.Equal("imogen", InteractionService.CoverTarget(trip, "nishimoto"));
        Assert.Null(InteractionService.CoverTarget(trip, "wilhelm"));

        var later = InteractionService.Start(trip, "imogen", "carla", At(6))!;
        var alibi = InteractionService.Ask(trip, later.Id, "imogen", "alibi", At(7))!;
        Assert.Equal("I was on the path — and so was Imogen Durham.", alibi.Answer);

        InteractionService.Ask(trip, later.Id, "carla", "alibi", At(8));
        var q2 = InteractionService.Ask(trip, later.Id, "imogen", "q2", At(9))!;
        Assert.Equal("I saw Imogen Durham on the side path.", q2.Answer);

        InteractionService.Ask(trip, later.Id, "carla", "q2", At(10));
        var q3 = InteractionService.Ask(trip, later.Id, "imogen", "q3", At(11))!;
        Assert.Equal("carla: thank you.", q3.Answer);

        // The earlier transcript is the contradiction, and it stays as it was said.
        Assert.Equal("carla: in my room.", early.Exchanges[0].Answer);
    }

    [Fact]
    public void A_scrub_still_moves_the_story_even_though_the_card_forgets_who()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "nishimoto", coverAlibi: "Ask {target}, not me.");

        var clue = trip.Mystery.Story.Clues[2].Id;
        AbilityService.TryFire(trip, "carla", "evidence_hand", At(5), "scrub", "yousef", clue);
        ScanShareService.Tamper(trip, clue, "scrub", "carla", "yousef", At(5));

        Assert.Null(trip.Mystery.Play.StateFor(clue)!.Tamper!.TargetCharacterId);
        Assert.Equal("yousef", InteractionService.CoverTarget(trip, "nishimoto"));
    }

    [Fact]
    public void A_jester_who_frames_themselves_starts_confessing()
    {
        var trip = In(MysteryPhase.Investigation);
        GiveQuestions(trip, "emilia", coverAlibi: "Between you and me, {target} was nowhere they should have been.");
        GiveQuestions(trip, "hugo", coverAlibi: "{target} did it.");

        var clue = trip.Mystery.Story.Clues[3].Id;
        AbilityService.TryFire(trip, "emilia", "self_frame", At(5), "subtle", "emilia", clue);

        Assert.Equal("emilia", InteractionService.CoverTarget(trip, "emilia"));
        Assert.Null(InteractionService.CoverTarget(trip, "hugo"));

        var q = trip.Mystery.Story.Character("emilia")!.Questions[0];
        Assert.Equal("Between you and me, Emilia Cruz was nowhere they should have been.",
            InteractionService.ResolveAnswer(trip, "emilia", q));
    }

    // ------------------------------------------------------------------------------------------
    //  The blame
    // ------------------------------------------------------------------------------------------

    private static void Convict(TripData trip, MysteryPhase phase, params string[] ids) =>
        trip.Mystery.Play.Trials.Add(new MysteryTrial { Phase = phase, Stage = MysteryTrialStage.Verdict, ConvictedCharacterIds = [.. ids] });

    [Fact]
    public void An_associate_who_took_the_blame_reads_as_a_killer_on_the_card()
    {
        var trip = In(MysteryPhase.Discussion2);

        Assert.False(TrialService.ShowsAsKiller(trip, "giuliana"));
        Assert.NotNull(AbilityService.TryFire(trip, "giuliana", "loyalty", At(0)));

        Assert.True(TrialService.ShowsAsKiller(trip, "giuliana"));
        Assert.False(TrialService.ShowsAsKiller(trip, "sutton"));
        Assert.True(TrialService.ShowsAsKiller(trip, "solomon"));

        // One charge between the two of them.
        Assert.Null(AbilityService.TryFire(trip, "sutton", "loyalty", At(1)));
    }

    [Fact]
    public void The_ending_admits_the_decoy_only_when_it_changed_the_result()
    {
        // Two real hands: a true win, and nothing to admit.
        var win = In(MysteryPhase.Reveal);
        Convict(win, MysteryPhase.Trial1, "solomon", "carla");
        var w = OutcomeService.Compute(win, At(0));
        Assert.True(w.TownWon);
        Assert.False(w.RevealDecoy);

        // One hand and a blaming associate: the room thought it had two, and it had one.
        var fooled = In(MysteryPhase.Reveal);
        AbilityService.TryFire(fooled, "giuliana", "loyalty", At(0));
        Convict(fooled, MysteryPhase.Trial1, "solomon");
        Convict(fooled, MysteryPhase.Trial2, "giuliana", "wilhelm");
        var f = OutcomeService.Compute(fooled, At(0));
        Assert.Equal(1, f.KillersConvicted);
        Assert.Equal(2, f.ShownKillersConvicted);
        Assert.False(f.TownWon);
        Assert.True(f.RevealDecoy);
        Assert.Equal(["giuliana"], OutcomeService.ConvictedDecoys(fooled).Select(c => c.Id));

        // Two hands and the associate: a win either way, the associate is never named as one.
        var anyway = In(MysteryPhase.Reveal);
        AbilityService.TryFire(anyway, "giuliana", "loyalty", At(0));
        Convict(anyway, MysteryPhase.Trial1, "solomon", "carla");
        Convict(anyway, MysteryPhase.Trial2, "giuliana");
        var a = OutcomeService.Compute(anyway, At(0));
        Assert.True(a.TownWon);
        Assert.False(a.RevealDecoy);

        // The associate alone: a loss that was a loss regardless.
        var lost = In(MysteryPhase.Reveal);
        AbilityService.TryFire(lost, "giuliana", "loyalty", At(0));
        Convict(lost, MysteryPhase.Trial1, "giuliana", "wilhelm");
        var l = OutcomeService.Compute(lost, At(0));
        Assert.False(l.TownWon);
        Assert.False(l.RevealDecoy);
    }

    // ------------------------------------------------------------------------------------------
    //  The detectives' answers
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void The_hard_question_comes_back_with_what_the_room_would_be_told()
    {
        var trip = In(MysteryPhase.Discussion2);
        AbilityService.TryFire(trip, "giuliana", "loyalty", At(0));

        // Remington carries the Hard Question; Molly and Martha carry Forensics and cannot ask it.
        Assert.Equal("killer", AbilityService.TryFire(trip, "remington", "killer_check", At(1), targetCharacterId: "giuliana")!.Result);
        Assert.Null(AbilityService.TryFire(trip, "molly", "killer_check", At(1), targetCharacterId: "solomon"));
        Assert.Null(AbilityService.TryFire(trip, "martha", "killer_check", At(1), targetCharacterId: "wilhelm"));

        var second = new TripData(); StoryLoader.SeedInto(second); CastingService.OpenDoors(second, new Random(1));
        second.Mystery.Phase = MysteryPhase.StudyScene;
        Assert.Equal("clean", AbilityService.TryFire(second, "remington", "killer_check", At(1), targetCharacterId: "wilhelm")!.Result);

        // Spent and answered, so it shows up beside the conversations.
        Assert.Single(InteractionService.ResultsFor(trip, "remington"));
        Assert.Equal("KILLER", AbilityService.ResultLabel("killer"));
    }

    [Fact]
    public void A_question_about_nobody_costs_nothing()
    {
        var trip = In(MysteryPhase.Discussion2);

        Assert.Null(AbilityService.TryFire(trip, "remington", "killer_check", At(1)));
        Assert.Null(AbilityService.TryFire(trip, "molly", "tamper_check", At(1)));
        Assert.Equal(1, AbilityService.ChargesRemaining(trip, "molly", AbilityService.AbilitiesFor(trip, "molly").First(a => a.Id == "tamper_check")));
    }

    [Fact]
    public void Forensics_reads_the_card()
    {
        var trip = In(MysteryPhase.Discussion2);
        var clues = trip.Mystery.Story.Clues;

        ScanShareService.Tamper(trip, clues[1].Id, "plant", "solomon", "imogen", At(0));
        ScanShareService.Tamper(trip, clues[2].Id, "scrub", "carla", null, At(0));
        ScanShareService.Tamper(trip, clues[3].Id, "blatant", "emilia", "emilia", At(0));

        Assert.Equal("untouched", AbilityService.TryFire(trip, "molly", "tamper_check", At(1), targetClueId: clues[0].Id)!.Result);
        Assert.Equal("planted", AbilityService.TryFire(trip, "martha", "tamper_check", At(1), targetClueId: clues[1].Id)!.Result);

        // Remington carries the Hard Question, not Forensics.
        Assert.Null(AbilityService.TryFire(trip, "remington", "tamper_check", At(1), targetClueId: clues[2].Id));
        Assert.Equal("scrubbed", AbilityService.ResultFor(trip, "tamper_check", null, clues[2].Id));
        Assert.Equal("planted", AbilityService.ResultFor(trip, "tamper_check", null, clues[3].Id));
    }

    // ------------------------------------------------------------------------------------------
    //  The introductions
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void Braun_goes_first_then_his_staff_then_the_guests()
    {
        var trip = In(MysteryPhase.Lobby);
        var order = PhaseService.IntroOrder(trip);

        Assert.Equal(25, order.Count);
        Assert.Equal("braun", order[0].Id);
        Assert.All(order.Skip(1).Take(3), c => Assert.Equal(MysteryStaffRole.Facilitator, c.Staff));
        Assert.All(order.Skip(4), c => Assert.False(c.IsStaff));
    }

    [Fact]
    public void Each_person_is_told_they_are_up_exactly_once()
    {
        var trip = In(MysteryPhase.Presentation);
        PhaseService.GoToPhase(trip, MysteryPhase.Introductions, At(0));

        Assert.Equal("braun", PhaseService.CurrentIntro(trip)!.Id);

        var count = 0;
        while (PhaseService.NextIntro(trip, At(++count))) { }
        Assert.Equal(24, count - 1);
        Assert.True(PhaseService.OnLastIntro(trip));

        var issues = trip.Mystery.Play.Objectives.Where(o => o.TemplateId == PhaseService.IntroTemplateId).ToList();
        Assert.Equal(25, issues.Count);
        Assert.All(issues, i => Assert.Single(i.CharacterIds));
        Assert.Equal(25, issues.Select(i => i.CharacterIds[0]).Distinct().Count());

        // Back and forward over somebody does not tell them twice.
        Assert.True(PhaseService.PreviousIntro(trip));
        Assert.True(PhaseService.NextIntro(trip, At(99)));
        Assert.Equal(25, trip.Mystery.Play.Objectives.Count(o => o.TemplateId == PhaseService.IntroTemplateId));

        // Only the person who is up can see it.
        var last = PhaseService.CurrentIntro(trip)!;
        Assert.Contains(ObjectiveBus.Inbox(trip, last.Id), o => o.TemplateId == PhaseService.IntroTemplateId);
        Assert.DoesNotContain(ObjectiveBus.Inbox(trip, "braun").Where(o => o.TemplateId == PhaseService.IntroTemplateId),
            o => o.CharacterIds.Contains(last.Id));
    }

    // ------------------------------------------------------------------------------------------
    //  The clock
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The study's card is the first clue and it is handed to everybody playing the moment the
    /// evening reaches the study — recorded as a scan, so it sits on the Clues tab like any other
    /// card. The four running the evening get nothing; they are not playing.
    /// </summary>
    [Fact]
    public void Reaching_the_study_hands_every_guest_the_studys_card()
    {
        var trip = In(MysteryPhase.Murder);
        var clueId = PhaseService.StudyClueId(trip)!;

        Assert.True(PhaseService.GoToPhase(trip, MysteryPhase.StudyScene, At(1)));

        Assert.All(trip.Mystery.Story.Guests, g => Assert.True(ScanShareService.HasScanned(trip, g.Id, clueId)));
        Assert.All(trip.Mystery.Story.StaffParts, s => Assert.False(ScanShareService.HasScanned(trip, s.Id, clueId)));

        // Idempotent: walking back in does not scan it twice.
        var scans = trip.Mystery.Play.ClueScans.Count;
        PhaseService.GoToPhase(trip, MysteryPhase.Murder, At(2));
        PhaseService.GoToPhase(trip, MysteryPhase.StudyScene, At(3));
        Assert.Equal(scans, trip.Mystery.Play.ClueScans.Count);
    }

    /// <summary>
    /// "Everyone to the study" goes to the twenty-one playing and to none of the four running
    /// it — the butler is reading the announcement off his own phone at that moment, and an
    /// instruction to go to the study on top of it is noise.
    /// </summary>
    [Fact]
    public void The_call_to_the_study_reaches_guests_and_not_the_house()
    {
        var trip = In(MysteryPhase.Introductions);
        foreach (var seat in trip.Mystery.Play.Cast) seat.JoinedAt ??= At(0);

        PhaseService.GoToPhase(trip, MysteryPhase.Murder, At(1));

        Assert.All(trip.Mystery.Story.Guests,
            g => Assert.Contains(ObjectiveBus.Inbox(trip, g.Id), o => o.TemplateId == "to-the-study"));
        Assert.All(trip.Mystery.Story.StaffParts,
            s => Assert.DoesNotContain(ObjectiveBus.Inbox(trip, s.Id), o => o.TemplateId == "to-the-study"));

        // One at a time: the oldest not yet done is the one being asked.
        var wilhelm = "wilhelm";
        ObjectiveBus.SendFreeText(trip, "Later", MysteryPhase.Murder, At(2), "host", MysteryAudience.Guests);
        Assert.Equal("to-the-study", ObjectiveBus.Current(trip, wilhelm)!.TemplateId);
        ObjectiveBus.Complete(trip, wilhelm, ObjectiveBus.Current(trip, wilhelm)!.Id);
        Assert.Equal("Later", ObjectiveBus.Current(trip, wilhelm)!.Text);
    }

    /// <summary>
    /// The nine cards are 1 to 9 in story order; typing the number at /join opens the card's page.
    /// </summary>
    [Fact]
    public void The_clue_cards_are_numbered_and_the_number_is_a_join_code()
    {
        var trip = In(MysteryPhase.Investigation);
        var clues = trip.Mystery.Story.Clues;

        for (var i = 0; i < clues.Count; i++)
            Assert.Equal((i + 1).ToString(), trip.Mystery.Play.StateFor(clues[i].Id)!.Token);

        var match = JoinCodes.Resolve(trip, " 1 ");
        Assert.Equal(CodeKind.Clue, match.Kind);
        Assert.Equal("1", match.ClueToken);
        Assert.Same(clues[0], ScanShareService.ClueForToken(trip, "1"));

        Assert.Equal(CodeKind.Unknown, JoinCodes.Resolve(trip, "10").Kind);
        Assert.Equal(CodeKind.MysteryParty, JoinCodes.Resolve(trip, trip.Mystery.Play.PartyCode).Kind);

        // A game that opened its doors under the old tokens gets the numbers on the way up.
        trip.Mystery.Play.ClueStates[0].Token = "XKQ7VW3TNA";
        Assert.True(CastingService.NumberTheClues(trip));
        Assert.Equal("1", trip.Mystery.Play.ClueStates[0].Token);
        Assert.False(CastingService.NumberTheClues(trip));
    }

    [Fact]
    public void Entering_a_phase_stamps_it_and_re_entering_does_not()
    {
        var trip = In(MysteryPhase.StudyScene);

        Assert.True(PhaseService.GoToPhase(trip, MysteryPhase.Investigation, At(0)));
        Assert.Equal(At(0), trip.Mystery.Play.PhaseEnteredAt);
        Assert.Equal(At(30), PhaseService.PhaseDeadline(trip));

        Assert.False(PhaseService.GoToPhase(trip, MysteryPhase.Investigation, At(7)));
        Assert.Equal(At(0), trip.Mystery.Play.PhaseEnteredAt);

        PhaseService.GoToPhase(trip, MysteryPhase.Trial1, At(40));
        Assert.Null(PhaseService.PhaseDeadline(trip));
    }

    // ------------------------------------------------------------------------------------------
    //  Older files
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void V35_reloads_the_story_and_puts_finished_conversations_away()
    {
        var seeded = new TripData { SchemaVersion = 34 };
        StoryLoader.SeedInto(seeded);
        seeded.Mystery.Story.Character("wilhelm")!.Questions[0].Prompt = "Where were you when the tower came down?";
        seeded.Mystery.Play.Interactions.Add(new MysteryInteraction
        {
            Id = "old", ACharacterId = "wilhelm", BCharacterId = "imogen", StartedAt = At(0), CompletedAt = At(1)
        });
        seeded.Mystery.Play.Interactions.Add(new MysteryInteraction
        {
            Id = "live", ACharacterId = "yousef", BCharacterId = "molly", StartedAt = At(2)
        });

        Assert.True(TripMigrations.Apply(seeded));
        Assert.Equal(TripMigrations.CurrentVersion, seeded.SchemaVersion);
        Assert.Contains(seeded.Mystery.Story.Slides, s => s.Figure == "role:killer");
        Assert.Contains("murdered", seeded.Mystery.Story.Character("wilhelm")!.Questions[0].Prompt);
        Assert.False(MysteryText.IsPlaceholder(seeded.Mystery.Story.Character("carla")!.Epilogue));

        Assert.Null(InteractionService.ShowingFor(seeded, "wilhelm"));
        Assert.Null(InteractionService.ShowingFor(seeded, "imogen"));
        Assert.NotNull(InteractionService.ShowingFor(seeded, "yousef"));

        var never = new TripData { SchemaVersion = 34 };
        TripMigrations.Apply(never);
        Assert.False(never.Mystery.Story.Seeded);
        Assert.Empty(never.Mystery.Story.Characters);
    }

    [Fact]
    public void V34_reloads_the_story_into_a_seeded_trip_and_leaves_an_unseeded_one_alone()
    {
        var seeded = new TripData { SchemaVersion = 33 };
        StoryLoader.SeedInto(seeded);
        var loyalty = seeded.Mystery.Story.Faction("minion")!.Abilities[0];
        loyalty.Modes = [new MysteryAbilityMode { Id = "shield", Name = "Shield", Text = "old" }];
        loyalty.Unlock = MysteryPhase.Discussion1;

        Assert.True(TripMigrations.Apply(seeded));
        Assert.Equal(TripMigrations.CurrentVersion, seeded.SchemaVersion);
        Assert.Empty(seeded.Mystery.Story.Faction("minion")!.Abilities[0].Modes);
        Assert.Equal(MysteryPhase.StudyScene, seeded.Mystery.Story.Faction("minion")!.Abilities[0].Unlock);
        Assert.Contains(seeded.Mystery.Story.Slides, s => s.Figure == "map");

        var never = new TripData { SchemaVersion = 33 };
        TripMigrations.Apply(never);
        Assert.False(never.Mystery.Story.Seeded);
        Assert.Empty(never.Mystery.Story.Characters);
    }

    /// <summary>
    /// The name of the old party phase is in a saved file in five places, and the enum converter
    /// throws on it. A trip saved during the party must still open — as the introductions.
    /// </summary>
    [Fact]
    public async Task A_file_saved_during_the_old_party_phase_opens_at_the_introductions()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chartertrip-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var options = new TripStoreOptions { DataRoot = dir, DebounceMilliseconds = 0 };

        const string json = """
            {
              "schemaVersion": 33,
              "mystery": {
                "phase": "mingling",
                "story": {
                  "seeded": false,
                  "objectives": [ { "id": "x", "phase": "mingling", "audience": "everyone" } ],
                  "factions": [ { "id": "f", "abilities": [ { "id": "a", "unlock": "mingling" } ] } ]
                },
                "play": {
                  "objectives": [ { "id": "o", "issuedInPhase": "mingling", "audience": "everyone" } ],
                  "trials": [ { "phase": "mingling" } ]
                }
              }
            }
            """;
        await File.WriteAllTextAsync(options.TripFilePath, json, Encoding.UTF8);

        await using var store = new JsonTripStore(
            Microsoft.Extensions.Options.Options.Create(options),
            new RecordingLogger<JsonTripStore>(),
            new FixedClock(DateTimeOffset.UnixEpoch));

        Assert.False(store.Status.Seeded);
        Assert.Equal(MysteryPhase.Introductions, store.Current.Mystery.Phase);
        Assert.Equal(MysteryPhase.Introductions, store.Current.Mystery.Story.Objectives[0].Phase);
        Assert.Equal(MysteryPhase.Introductions, store.Current.Mystery.Play.Objectives[0].IssuedInPhase);
        Assert.Equal(MysteryPhase.Introductions, store.Current.Mystery.Play.Trials[0].Phase);
        Assert.Equal(MysteryPhase.Introductions, store.Current.Mystery.Story.Factions[0].Abilities[0].Unlock);
    }
}
