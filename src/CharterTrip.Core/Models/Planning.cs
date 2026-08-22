using System.Text.Json.Serialization;

namespace CharterTrip.Core.Models;

public sealed class ChecklistGroup
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ChecklistItem> Items { get; set; } = [];
}

public sealed class ChecklistItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";

    /// <summary>
    /// Phase 5 makes packing per-person (everyone packs their own bag), so this becomes
    /// a set of RosterPerson ids rather than one shared bool.
    /// </summary>
    public bool Done { get; set; }
}




