using CharterTrip.Core.Models;
using CharterTrip.Core.Services;

namespace CharterTrip.Tests;

/// <summary>
/// This is the check that runs before a photo file is deleted, so the asymmetry matters: a
/// leftover file costs a few hundred KB, a file deleted while a clue still points at it is a
/// blank square on the projector.
/// </summary>
public class TripMediaTests
{
    [Theory]
    [InlineData("/photos/abc123.jpg", "abc123.jpg")]
    [InlineData("/photos/a.png", "a.png")]
    public void Recognises_a_stored_photo(string value, string expected) =>
        Assert.Equal(expected, TripMedia.IdFrom(value));

    /// <summary>
    /// A pasted link is somebody else's file. Treating one as ours would mean issuing a delete
    /// for a path outside the photo folder the first time anyone pasted a URL.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://example.com/photos/abc.jpg")]
    [InlineData("photos/abc.jpg")]
    [InlineData("/images/abc.jpg")]
    [InlineData("/photos/")]
    public void Leaves_anything_that_is_not_ours_alone(string? value) =>
        Assert.Null(TripMedia.IdFrom(value));

    /// <summary>
    /// The photo store rejects a traversing id on the way in as well, but a value that arrived
    /// through a text box someone typed into should never reach it in the first place.
    /// </summary>
    [Theory]
    [InlineData("/photos/../../trip.json")]
    [InlineData("/photos/sub/abc.jpg")]
    [InlineData("/photos/..")]
    public void Refuses_an_id_that_tries_to_leave_the_photo_folder(string value) =>
        Assert.Null(TripMedia.IdFrom(value));

    [Fact]
    public void Finds_a_photo_on_a_clue_and_on_an_answer()
    {
        var trip = TripWithBoard();
        trip.Jeopardy.Categories[0].Clues[0].ClueImage = TripMedia.PathFor("clue.jpg");
        trip.Jeopardy.Categories[0].Clues[0].ResponseImage = TripMedia.PathFor("answer.jpg");

        var found = TripMedia.ReferencedIn(trip);

        Assert.Contains("clue.jpg", found);
        Assert.Contains("answer.jpg", found);
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void Ignores_pasted_links()
    {
        var trip = TripWithBoard();
        trip.Jeopardy.Categories[0].Clues[0].ClueImage = "https://example.com/cat.jpg";

        Assert.Empty(TripMedia.ReferencedIn(trip));
    }

    /// <summary>
    /// Slides and teams keep a bare id rather than a path. Neither is wired up to the uploader
    /// yet, and this is what stops the one that goes first from having its photos deleted by
    /// the one that was already here.
    /// </summary>
    [Fact]
    public void Finds_the_bare_ids_that_slides_and_teams_keep()
    {
        var trip = TripWithBoard();
        trip.Slides.Add(new CarouselSlide { Id = "s1", Kind = "photo", PhotoId = "slide.jpg" });
        trip.Teams.Add(new Team { Id = "t1", PhotoId = "team.jpg" });

        var found = TripMedia.ReferencedIn(trip);

        Assert.Contains("slide.jpg", found);
        Assert.Contains("team.jpg", found);
    }

    /// <summary>
    /// Two fields pointing at one upload is easy to do by pasting a path from one box into
    /// another, and is exactly the case where clearing one field must not delete the file.
    /// </summary>
    [Fact]
    public void One_photo_used_twice_is_still_referenced_after_one_use_goes()
    {
        var trip = TripWithBoard();
        var shared = TripMedia.PathFor("shared.jpg");
        trip.Jeopardy.Categories[0].Clues[0].ClueImage = shared;
        trip.Jeopardy.Categories[0].Clues[1].ClueImage = shared;

        trip.Jeopardy.Categories[0].Clues[0].ClueImage = "";

        Assert.Contains("shared.jpg", TripMedia.ReferencedIn(trip));
    }

    /// <summary>
    /// A face uploaded for one of Police Sketch's characters is a file the trip is using. Without
    /// this, replacing a second character's picture would delete the first one — release decides
    /// by asking what the trip still points at.
    /// </summary>
    [Fact]
    public void Finds_a_face_uploaded_for_a_sketch_character()
    {
        var trip = TripWithBoard();
        trip.Party.Sketch.Characters.Add(new SketchCharacter { Name = "Shrek", ImageUrl = TripMedia.PathFor("shrek.jpg") });
        trip.Party.Sketch.Characters.Add(new SketchCharacter { Name = "Snoopy", ImageUrl = "https://example.com/snoopy.png" });
        trip.Party.Sketch.Characters.Add(new SketchCharacter { Name = "Pikachu" });

        var found = TripMedia.ReferencedIn(trip);

        Assert.Contains("shrek.jpg", found);
        Assert.Single(found);       // the pasted link is not ours, and the empty one is nothing
    }

    [Fact]
    public void A_trip_with_no_photos_references_none() =>
        Assert.Empty(TripMedia.ReferencedIn(TripWithBoard()));

    // ------------------------------------------------------------- what it is

    [Theory]
    [InlineData("/photos/a.mp4")]
    [InlineData("/photos/a.webm")]
    [InlineData("/photos/a.mov")]
    [InlineData("/photos/a.m4v")]
    [InlineData("/photos/a.ogv")]
    [InlineData("/photos/A.MP4")]
    [InlineData("https://example.com/clip.mp4")]
    public void Knows_a_video_when_it_sees_one(string value) =>
        Assert.Equal(MediaKind.Video, TripMedia.KindOf(value));

    [Theory]
    [InlineData("/photos/a.jpg")]
    [InlineData("/photos/a.png")]
    [InlineData("/photos/a.gif")]
    [InlineData("https://example.com/photo.jpg")]
    public void Knows_a_picture_when_it_sees_one(string value) =>
        Assert.Equal(MediaKind.Image, TripMedia.KindOf(value));

    /// <summary>
    /// Every pasted link was an image before video existed, and a link with no extension is far
    /// likelier to be a picture than a playable video file. Guessing video would turn those into
    /// an empty player that fails silently.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/some/album/entry")]
    [InlineData("https://photos.example.com/abc123")]
    public void An_unrecognisable_link_is_treated_as_a_picture(string value) =>
        Assert.Equal(MediaKind.Image, TripMedia.KindOf(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_nothing(string? value) =>
        Assert.Equal(MediaKind.None, TripMedia.KindOf(value));

    /// <summary>
    /// A pasted link routinely ends <c>.jpg?width=800</c>, and asking the filesystem for the
    /// extension of that gets you <c>.jpg?width=800</c>.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/clip.mp4?token=abc", MediaKind.Video)]
    [InlineData("https://example.com/clip.mp4#t=10", MediaKind.Video)]
    [InlineData("https://example.com/photo.jpg?width=800", MediaKind.Image)]
    public void A_query_string_does_not_hide_the_extension(string value, MediaKind expected) =>
        Assert.Equal(expected, TripMedia.KindOf(value));

    /// <summary>A dot in a folder name is not an extension.</summary>
    [Fact]
    public void A_dot_before_the_last_slash_is_not_an_extension() =>
        Assert.Equal(MediaKind.Image, TripMedia.KindOf("https://ex.ample.com/photos/whatever"));

    [Fact]
    public void A_video_clue_is_still_a_reference_to_release()
    {
        var trip = TripWithBoard();
        trip.Jeopardy.Categories[0].Clues[0].ClueImage = TripMedia.PathFor("clip.mp4");

        Assert.Contains("clip.mp4", TripMedia.ReferencedIn(trip));
    }

    private static TripData TripWithBoard() => new()
    {
        Jeopardy = new JeopardyBoard
        {
            Categories =
            [
                new JeopardyCategory
                {
                    Name = "Pictures",
                    Clues = [new JeopardyClue { Id = "c1" }, new JeopardyClue { Id = "c2" }]
                }
            ]
        }
    };
}
