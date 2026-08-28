using System.Reflection;
using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

public class TripReplaceTests
{
    /// <summary>
    /// The failure this exists to prevent: someone adds a section to TripData, the import page
    /// keeps working, and that one section quietly survives every import — the live site ends up
    /// showing an itinerary from the uploaded file and a Jeopardy board from the old one.
    ///
    /// Reflection rather than a list of asserts, so a new property fails here on the day it is
    /// added rather than the day someone imports.
    /// </summary>
    [Fact]
    public void Every_section_of_a_trip_is_carried_over()
    {
        var source = Populated();
        var target = new TripData();

        TripReplace.Overwrite(target, source);

        foreach (var property in Sections())
            Assert.Equal(property.GetValue(source), property.GetValue(target));
    }

    /// <summary>
    /// Revision and UpdatedUtc describe this store's history, not the uploaded file's. Copying
    /// them would let an import wind the revision backwards, which is the one number anybody
    /// debugging a lost edit actually trusts.
    /// </summary>
    [Fact]
    public void The_stores_own_bookkeeping_is_left_alone()
    {
        var target = new TripData { Revision = 88, UpdatedUtc = DateTimeOffset.UnixEpoch };

        TripReplace.Overwrite(target, Populated());

        Assert.Equal(88, target.Revision);
        Assert.Equal(DateTimeOffset.UnixEpoch, target.UpdatedUtc);
    }

    /// <summary>
    /// Pages hold the object handed out by ITripStore.Current. Replacing the reference instead
    /// of its contents would leave every open tab editing a document nothing saves any more.
    /// </summary>
    [Fact]
    public void The_target_document_is_the_same_object_afterwards()
    {
        var target = new TripData();
        var before = target;

        TripReplace.Overwrite(target, Populated());

        Assert.Same(before, target);
    }

    /// <summary>
    /// Every property that carries trip content — which is everything except the two the store
    /// stamps itself. Named rather than filtered by type, so a plain string added to TripData is
    /// covered too.
    /// </summary>
    private static IEnumerable<PropertyInfo> Sections() =>
        typeof(TripData).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not (nameof(TripData.Revision) or nameof(TripData.UpdatedUtc)));

    /// <summary>A trip whose every section is a distinct object, so a missed copy cannot pass.</summary>
    private static TripData Populated() => new()
    {
        SchemaVersion = 7,
        Revision = 41,
        UpdatedUtc = new DateTimeOffset(2026, 8, 28, 17, 0, 0, TimeSpan.Zero),
        Trip = new TripInfo { Name = "Charter Trip" },
        Venue = new VenueInfo { Name = "Braun Manor" },
        Slides = [new CarouselSlide { Id = "slide" }],
        Teams = [new Team { Id = "team", Name = "The Lambdas" }],
        Roster = [new RosterPerson { Id = "person", Name = "Kyle", TeamId = "team" }],
        Itinerary = [new ItineraryDay { Id = "day", Day = "Friday" }],
        Games = [new Game { Id = "game", Name = "Beer Run" }],
        Scores = [new ScoreEntry { Id = "score", TeamId = "team", Points = 10 }],
        Superlatives = [new Superlative { Id = "sup", Title = "Most likely to nap" }],
        Jeopardy = new JeopardyBoard { Title = "Charter Jeopardy" },
        Mystery = new MysteryState { Phase = MysteryPhase.Investigation }
    };
}
