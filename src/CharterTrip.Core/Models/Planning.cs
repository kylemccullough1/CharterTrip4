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

public sealed class BringItem
{
    public string Id { get; set; } = "";
    public string Who { get; set; } = "";
    public string Items { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class BudgetLine
{
    public string Id { get; set; } = "";
    public string Item { get; set; } = "";
    public decimal Unit { get; set; }
    public decimal Qty { get; set; }
    public string Store { get; set; } = "";
    public string Category { get; set; } = "";

    /// <summary>Computed, so it is not persisted.</summary>
    [JsonIgnore]
    public decimal Total => Unit * Qty;
}

public sealed class ShoppingList
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<ShoppingItem> Items { get; set; } = [];
}

public sealed class ShoppingItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public string Qty { get; set; } = "";
    public bool Done { get; set; }
}
