using CharterTrip.Core.Models;
using CharterTrip.Infrastructure.Storage;

namespace CharterTrip.Tests;

/// <summary>
/// What a carpool knows about itself: what it is called, when it leaves, when it arrives —
/// facts that belong to the car rather than to any one passenger, and that anyone may write.
/// </summary>
public class CarpoolTests
{
    [Fact]
    public void A_car_nobody_has_written_on_is_empty()
    {
        Assert.True(new Car { Slot = 3 }.IsEmpty);
    }

    [Theory]
    [InlineData("Ali's car", "", "")]
    [InlineData("", "7:30 PM", "")]
    [InlineData("", "", "Friday")]
    public void One_fact_is_enough_to_keep_a_car(string name, string eta, string etaDay)
    {
        var car = new Car { Slot = 1, Name = name, Eta = eta, EtaDay = etaDay };

        Assert.False(car.IsEmpty);
    }

    /// <summary>
    /// The day and the time are two answers, so saying one does not wipe the other — a car can
    /// know it is coming Saturday before it knows when.
    /// </summary>
    [Fact]
    public void A_car_can_name_the_day_before_it_knows_the_time()
    {
        var car = new Car { Slot = 1, EtaDay = "Saturday" };

        Assert.False(car.IsEmpty);
        Assert.Equal("Saturday", car.EtaDay);
        Assert.Equal("", car.Eta);
    }

    /// <summary>Blanking every field is how a car is thrown away, so whitespace cannot keep it alive.</summary>
    [Fact]
    public void Whitespace_does_not_count_as_something_written()
    {
        var car = new Car { Slot = 1, Name = "   ", Eta = " ", EtaDay = "\t" };

        Assert.True(car.IsEmpty);
    }

    /// <summary>
    /// When a car leaves is not the car's business: every passenger answers that on their own
    /// row, and a second departure time here would only be a chance for the two to disagree.
    /// </summary>
    [Fact]
    public void A_car_says_when_it_arrives_and_leaves_departure_to_its_passengers()
    {
        Assert.Null(typeof(Car).GetProperty("LeaveAt"));
        Assert.NotNull(typeof(TravelRow).GetProperty(nameof(TravelRow.DepartAt)));
    }

    [Fact]
    public void A_trip_starts_with_no_cars_named()
    {
        Assert.Empty(new TravelPlan().Cars);
    }

    /// <summary>
    /// An older file predates the cars list entirely. It should load and migrate without one
    /// rather than being treated as damaged.
    /// </summary>
    [Fact]
    public void A_file_from_before_cars_existed_migrates_to_an_empty_list()
    {
        var trip = new TripData { SchemaVersion = 15 };

        TripMigrations.Apply(trip);

        Assert.Equal(TripMigrations.CurrentVersion, trip.SchemaVersion);
        Assert.Empty(trip.Travel.Cars);
    }

    /// <summary>
    /// A car's table does not draw the "Can depart" column, but hiding it is all that happens:
    /// joining a car and leaving one only ever writes <see cref="TravelRow.CarColor"/>, so the
    /// answer somebody gave is still there when the column comes back.
    /// </summary>
    [Fact]
    public void Joining_a_car_and_leaving_it_again_keeps_what_you_answered()
    {
        var row = new TravelRow { PersonId = "evie", DepartAt = "Morning?", LeavingFrom = "OKC" };

        row.CarColor = 1;
        row.CarColor = 0;

        Assert.Equal("Morning?", row.DepartAt);
        Assert.Equal("OKC", row.LeavingFrom);
    }

    /// <summary>
    /// And the row survives the trip. Rows are pruned once every field is blank, so a departure
    /// time has to count as something worth keeping — otherwise leaving a car would take the
    /// answer with it.
    /// </summary>
    [Fact]
    public void A_departure_time_alone_keeps_a_row_after_its_car_is_given_up()
    {
        var row = new TravelRow { PersonId = "evie", DepartAt = "Morning?", CarColor = 1 };

        row.CarColor = 0;

        Assert.False(row.IsEmpty);
    }

    /// <summary>
    /// A carpool's table asks what you are bringing where every other table asks for a note.
    /// They are two fields, so answering one cannot overwrite the other — and a remark written
    /// before joining a car is not silently relabelled as what that person is bringing.
    /// </summary>
    [Fact]
    public void What_you_are_bringing_and_a_note_are_two_different_answers()
    {
        var row = new TravelRow { PersonId = "evie", Notes = "Leave anytime" };

        row.CarColor = 1;
        row.Bringing = "The good speaker";

        Assert.Equal("Leave anytime", row.Notes);
        Assert.Equal("The good speaker", row.Bringing);
    }

    /// <summary>Rows are pruned when blank, so what you are bringing has to count as an answer.</summary>
    [Fact]
    public void Bringing_something_alone_keeps_a_row()
    {
        var row = new TravelRow { PersonId = "kyle", Bringing = "Cooler" };

        Assert.False(row.IsEmpty);
    }

    [Fact]
    public void A_row_with_nothing_on_it_at_all_is_still_empty()
    {
        Assert.True(new TravelRow { PersonId = "kyle" }.IsEmpty);
    }

    /// <summary>A car is identified by its palette slot, the same handle a passenger points at.</summary>
    [Fact]
    public void A_car_is_found_by_the_slot_its_passengers_point_at()
    {
        var travel = new TravelPlan
        {
            Cars = [new Car { Slot = 2, Name = "The long way round" }],
            Rows = [new TravelRow { PersonId = "kyle", CarColor = 2 }]
        };

        var mine = travel.Cars.Single(c => c.Slot == travel.Rows.Single().CarColor);

        Assert.Equal("The long way round", mine.Name);
    }
}
