using CharterTrip.Core.Models;

namespace CharterTrip.Web.Components.Shared;

/// <summary>A team and what they just earned. What a round hands to the card that celebrates it.</summary>
public sealed record ScoreRow(Team Team, int Points);
