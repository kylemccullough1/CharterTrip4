using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

/// <summary>
/// Getting everyone to Denison: where they are leaving from, when they can go, and who is in
/// whose car.
///
/// This replaces a shared spreadsheet whose carpools were expressed by highlighting rows in
/// matching colours. That is a good idea badly served by a spreadsheet — it relies on everyone
/// picking the same shade — so the colour is derived from the car name here instead. Type the
/// same car, get the same colour, automatically.
/// </summary>
public sealed class TravelPlan
{
    public string Destination { get; set; } = "";
    public string Address { get; set; } = "";
    public string CheckInTime { get; set; } = "";
    public string CheckOutTime { get; set; } = "";

    /// <summary>
    /// Only people who have filled something in. The page lists the whole roster and creates a
    /// row the first time somebody types into one, so an empty trip does not carry twenty-five
    /// blank records around.
    /// </summary>
    public List<TravelRow> Rows { get; set; } = [];

    /// <summary>
    /// What each carpool has to say for itself. Same rule as <see cref="Rows"/>: a car exists
    /// here only once somebody has written something on it, so the ten palette slots do not
    /// arrive as ten empty records.
    /// </summary>
    public List<Car> Cars { get; set; } = [];
}

/// <summary>
/// A carpool, as opposed to a seat in one. The colour slot is the carpool's identity — that is
/// what a passenger points at — and these are the facts that belong to the car itself rather
/// than to any one person in it: what everyone calls it, and when it expects to arrive.
///
/// When it leaves is deliberately not here. Every passenger already answers that on their own
/// row, and a second departure time on the car would only ever be a chance for the two to
/// disagree.
///
/// Anyone may write these. A carpool is not committee business: the people in the car are the
/// ones who know when they are leaving, and making them ask an admin to write it down would be
/// a good way to ensure it never gets written down.
/// </summary>
public sealed class Car
{
    /// <summary>Which slot in the colour palette this car is, matching <see cref="TravelRow.CarColor"/>.</summary>
    public int Slot { get; set; }

    /// <summary>What the car is called. Blank falls back to the colour it is drawn in.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Which day it expects to arrive, as the name of the day — "Friday". Not every car sets
    /// off on the same one, and "7:30" on its own cannot say which.
    /// </summary>
    public string EtaDay { get; set; } = "";

    /// <summary>
    /// When it expects to reach the house. Stored the way <see cref="TravelRow.DepartAt"/> is —
    /// "7:30 PM" — because the two are picked with the same control.
    /// </summary>
    public string Eta { get; set; } = "";

    /// <summary>True when there is nothing here worth keeping.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Eta) &&
        string.IsNullOrWhiteSpace(EtaDay);
}

/// <summary>One person's travel line, keyed to the roster so the table follows who is coming.</summary>
public sealed class TravelRow
{
    public string PersonId { get; set; } = "";
    public string LeavingFrom { get; set; } = "";

    /// <summary>Free text on purpose — "Anytime", "12:30-1", "Morning?" are all real answers.</summary>
    public string DepartAt { get; set; } = "";

    public string Dietary { get; set; } = "";
    public string Notes { get; set; } = "";

    /// <summary>
    /// What this person is putting in the car — the cooler, the aux cord, the good speaker.
    /// Its own field rather than a second use of <see cref="Notes"/>: a carpool's table asks
    /// this where every other table asks for a note, and one box answering two questions would
    /// mean writing one over the other depending on which table you happened to be looking at.
    /// </summary>
    public string Bringing { get; set; } = "";

    /// <summary>
    /// Which carpool, as a slot in the ten-colour palette. Zero means no car yet.
    ///
    /// A slot rather than a name, because naming a car was a step that bought nothing: the
    /// spreadsheet this replaces identified a carpool purely by the colour its rows were
    /// highlighted in, and the people in it are the only label anybody actually reads.
    /// </summary>
    public int CarColor { get; set; }

    /// <summary>True when there is nothing here worth keeping.</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(LeavingFrom) && string.IsNullOrWhiteSpace(DepartAt) &&
        string.IsNullOrWhiteSpace(Dietary) && string.IsNullOrWhiteSpace(Notes) &&
        string.IsNullOrWhiteSpace(Bringing) && CarColor == 0;
}
