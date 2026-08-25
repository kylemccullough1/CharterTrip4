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

    public List<DriveTime> DriveTimes { get; set; } = [];

    /// <summary>
    /// Only people who have filled something in. The page lists the whole roster and creates a
    /// row the first time somebody types into one, so an empty trip does not carry twenty-five
    /// blank records around.
    /// </summary>
    public List<TravelRow> Rows { get; set; } = [];
}

public sealed class DriveTime
{
    public string From { get; set; } = "";
    public string Duration { get; set; } = "";
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
        CarColor == 0;
}
