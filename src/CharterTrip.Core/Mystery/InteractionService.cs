using CharterTrip.Core.Models;

namespace CharterTrip.Core.Mystery;

/// <summary>
/// The conversations: what happens when one guest scans another's badge after the murder.
///
/// Every guest carries three questions anybody may ask them — where they were when Braun was
/// murdered, and two more about their evening — and a scan opens a private session
/// in which the two take turns asking until each has asked the other's three. The answers are
/// written, not typed: the phone shows the answering player what to say, and the asking player
/// sees it too, so the conversation happens out loud and the phone only keeps the record.
///
/// The record is the point. A killer's answers change the moment their tamper fires, and a
/// transcript taken before that moment is not rewritten — which is how the room catches them.
/// </summary>
public static class InteractionService
{
    /// <summary>The abilities whose use turns a character's answers into their cover story.</summary>
    private static readonly string[] TamperAbilities = ["evidence_hand", "self_frame"];

    // ------------------------------------------------------------------------------------------
    //  Starting
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a badge scan opens a conversation rather than just recording a meeting. Not before
    /// the murder: there is no "where were you when" to ask about yet, and the introductions are
    /// meant to be said out loud, not read off a phone.
    /// </summary>
    public static bool CanTalk(TripData trip) => PhaseService.RolesRevealed(trip);

    /// <summary>
    /// Open the conversation between two guests, or hand back the one they already have.
    ///
    /// The scanner asks first. Refused for staff on either side, for a scan of your own badge,
    /// and while either of them is mid-conversation with somebody else — one at a time is what
    /// makes it a conversation. The meeting is recorded either way, so the Met list stays honest.
    /// </summary>
    public static MysteryInteraction? Start(TripData trip, string scannerId, string scannedId, DateTimeOffset now)
    {
        if (scannerId == scannedId) return null;

        var story = trip.Mystery.Story;
        var a = story.Character(scannerId);
        var b = story.Character(scannedId);
        if (a is null || b is null) return null;

        // The meeting counts whatever else happens: scanning a facilitator is still meeting them.
        ScanShareService.RecordMeeting(trip, scannerId, scannedId, now);

        if (a.IsStaff || b.IsStaff || !CanTalk(trip)) return null;

        if (Between(trip, scannerId, scannedId) is { } existing) return existing;

        if (OpenFor(trip, scannerId) is not null || OpenFor(trip, scannedId) is not null) return null;

        // A finished conversation still on either screen is put away by the new one starting: the
        // scan is the clearest possible "I'm done reading that".
        foreach (var id in new[] { scannerId, scannedId })
            foreach (var old in trip.Mystery.Play.Interactions.Where(i => !i.IsOpen && i.ShowingTo(id)))
                old.ClosedBy.Add(id);

        var session = new MysteryInteraction
        {
            Id = Guid.NewGuid().ToString("n")[..12],
            ACharacterId = scannerId,
            BCharacterId = scannedId,
            StartedAt = now
        };

        trip.Mystery.Play.Interactions.Add(session);
        return session;
    }

    /// <summary>The conversation this character is in right now, if any.</summary>
    public static MysteryInteraction? OpenFor(TripData trip, string characterId) =>
        trip.Mystery.Play.Interactions.FirstOrDefault(i => i.IsOpen && i.Involves(characterId));

    /// <summary>
    /// What this character's phone should be showing instead of its tabs: the conversation they
    /// are in, or the one that just finished and they have not yet put away. Newest first, so a
    /// backlog of unread ones is read one at a time rather than all at once.
    /// </summary>
    public static MysteryInteraction? ShowingFor(TripData trip, string characterId) =>
        OpenFor(trip, characterId)
        ?? trip.Mystery.Play.Interactions
            .Where(i => i.ShowingTo(characterId))
            .OrderByDescending(i => i.CompletedAt)
            .FirstOrDefault();

    /// <summary>Put a finished conversation away, for this side only. The other side keeps reading.</summary>
    public static bool Dismiss(TripData trip, string sessionId, string characterId)
    {
        var session = ById(trip, sessionId);
        if (session is null || session.IsOpen || !session.Involves(characterId)) return false;
        if (session.ClosedBy.Contains(characterId)) return false;

        session.ClosedBy.Add(characterId);
        return true;
    }

    /// <summary>The one conversation these two have had, or null. Read from either side.</summary>
    public static MysteryInteraction? Between(TripData trip, string aId, string bId) =>
        trip.Mystery.Play.Interactions.FirstOrDefault(i =>
            (i.ACharacterId == aId && i.BCharacterId == bId) ||
            (i.ACharacterId == bId && i.BCharacterId == aId));

    public static MysteryInteraction? ById(TripData trip, string sessionId) =>
        trip.Mystery.Play.Interactions.FirstOrDefault(i => i.Id == sessionId);

    /// <summary>Everything this character has been told, newest conversation first.</summary>
    public static IReadOnlyList<MysteryInteraction> SessionsFor(TripData trip, string characterId) =>
        trip.Mystery.Play.Interactions
            .Where(i => i.Involves(characterId))
            .OrderByDescending(i => i.StartedAt)
            .ToList();

    // ------------------------------------------------------------------------------------------
    //  Asking
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Whose turn it is. The scanner asks first and they alternate, so it is the scanner on every
    /// even count. Null once the conversation is over.
    /// </summary>
    public static string? NextAsker(TripData trip, MysteryInteraction session)
    {
        if (!session.IsOpen) return null;

        var a = session.Exchanges.Count % 2 == 0 ? session.ACharacterId : session.BCharacterId;

        // If the natural asker has nothing left to ask, the other one carries on until they are
        // out too. A story where one side has fewer questions written should still finish.
        if (Askable(trip, session, a).Count > 0) return a;

        var other = session.Other(a)!;
        return Askable(trip, session, other).Count > 0 ? other : null;
    }

    /// <summary>What this character may still ask the other one: their questions, minus the asked.</summary>
    public static IReadOnlyList<MysteryQuestion> Askable(TripData trip, MysteryInteraction session, string askerId)
    {
        var otherId = session.Other(askerId);
        if (otherId is null) return [];

        var other = trip.Mystery.Story.Character(otherId);
        if (other is null) return [];

        var asked = session.Exchanges
            .Where(e => e.AskerCharacterId == askerId)
            .Select(e => e.QuestionId)
            .ToHashSet(StringComparer.Ordinal);

        return other.Questions.Where(q => !asked.Contains(q.Id)).ToList();
    }

    /// <summary>
    /// Ask one question. Refused out of turn, or for a question that is not the other person's
    /// or has already been asked. The answer is resolved and written down now — see
    /// <see cref="ResolveAnswer"/> — and the conversation closes itself when there is nothing
    /// left for either side to ask.
    /// </summary>
    public static MysteryExchange? Ask(TripData trip, string sessionId, string askerId, string questionId, DateTimeOffset now)
    {
        var session = ById(trip, sessionId);
        if (session is null || !session.IsOpen) return null;
        if (NextAsker(trip, session) != askerId) return null;

        var question = Askable(trip, session, askerId).FirstOrDefault(q => q.Id == questionId);
        if (question is null) return null;

        var answererId = session.Other(askerId)!;

        var exchange = new MysteryExchange
        {
            AskerCharacterId = askerId,
            QuestionId = question.Id,
            Prompt = question.Prompt,
            Answer = ResolveAnswer(trip, answererId, question),
            At = now
        };

        session.Exchanges.Add(exchange);

        if (NextAsker(trip, session) is null) session.CompletedAt = now;

        return exchange;
    }

    /// <summary>
    /// End a conversation early — for a partner who walked away, or a facilitator tidying up.
    /// Whoever walked away has put it away too; the other side sees it ended and closes it when
    /// they have read what there was.
    /// </summary>
    public static bool Abandon(TripData trip, string sessionId, DateTimeOffset now, string? byCharacterId = null)
    {
        var session = ById(trip, sessionId);
        if (session is null || !session.IsOpen) return false;

        session.CompletedAt = now;
        if (byCharacterId is not null && session.Involves(byCharacterId)) session.ClosedBy.Add(byCharacterId);
        return true;
    }

    /// <summary>Star or unstar one answer, for this character only. Each side keeps their own stars.</summary>
    public static bool ToggleStar(TripData trip, string sessionId, int exchangeIndex, string characterId)
    {
        var session = ById(trip, sessionId);
        if (session is null || !session.Involves(characterId)) return false;
        if (exchangeIndex < 0 || exchangeIndex >= session.Exchanges.Count) return false;

        var stars = session.Exchanges[exchangeIndex].StarredBy;
        if (!stars.Remove(characterId)) stars.Add(characterId);
        return true;
    }

    // ------------------------------------------------------------------------------------------
    //  What gets said
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Who this character's lie points at, if their story has one in it: the guest their faction's
    /// tamper named. A killer's plant or scrub is shared across the three hands, so all three
    /// alibis move together the moment any of them fires; a jester's self-frame points at the
    /// jester. Null while nothing has been tampered with, which is when the plain answers stand.
    ///
    /// Read from the ability log rather than from the card, because scrubbing a card wipes its
    /// target — the card no longer says who, but the story still has to.
    /// </summary>
    public static string? CoverTarget(TripData trip, string characterId)
    {
        var character = trip.Mystery.Story.Character(characterId);
        if (character is null || character.IsStaff) return null;

        var use = trip.Mystery.Play.AbilityUses
            .Where(u => TamperAbilities.Contains(u.AbilityId) && u.TargetCharacterId is not null)
            .Where(u => u.AbilityId == "self_frame"
                ? u.ByCharacterId == characterId
                : u.FactionId == character.FactionId)
            .OrderBy(u => u.At)
            .FirstOrDefault();

        return use?.TargetCharacterId;
    }

    /// <summary>
    /// The answer as it stands right now: the cover version with the framed guest's name in it once
    /// the story has a lie in it, the plain one until then. Questions without a cover — every
    /// useless one, and everything an innocent says — never change.
    /// </summary>
    public static string ResolveAnswer(TripData trip, string answererId, MysteryQuestion question)
    {
        if (!question.HasCover) return question.Answer;
        if (CoverTarget(trip, answererId) is not { } targetId) return question.Answer;

        var target = trip.Mystery.Story.Character(targetId)?.Name ?? "somebody";
        return question.CoverAnswer!.Replace("{target}", target, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the game itself has told this character: every ability they fired that came back with
    /// an answer. The detectives' forensics and Hard Question land here, beside the conversations.
    /// </summary>
    public static IReadOnlyList<MysteryAbilityUse> ResultsFor(TripData trip, string characterId) =>
        trip.Mystery.Play.AbilityUses
            .Where(u => u.ByCharacterId == characterId && u.Result is not null)
            .OrderByDescending(u => u.At)
            .ToList();
}
