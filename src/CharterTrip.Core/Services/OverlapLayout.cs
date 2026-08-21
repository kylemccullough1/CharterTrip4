using CharterTrip.Core.Models;

namespace CharterTrip.Core.Services;

/// <summary>An item plus where it sits horizontally when things clash.</summary>
public sealed record PositionedItem(ItineraryItem Item, int Column, int ColumnCount);

/// <summary>
/// Two things happening at once should sit side by side rather than on top of each other,
/// the way any calendar app does it.
///
/// Items are grouped into clusters of overlapping events; within a cluster each item takes the
/// leftmost column that is free by the time it starts, and every item in the cluster is told how
/// many columns the cluster ended up needing so it can size itself.
/// </summary>
public static class OverlapLayout
{
    public static IReadOnlyList<PositionedItem> Arrange(IEnumerable<ItineraryItem> items)
    {
        var scheduled = items
            .Where(i => i.IsScheduled)
            .OrderBy(i => i.StartMinutes!.Value)
            .ThenByDescending(i => i.DurationMinutes)
            .ToList();

        var result = new List<PositionedItem>(scheduled.Count);
        var cluster = new List<(ItineraryItem Item, int Column)>();
        var columnEnds = new List<int>();
        var clusterEnd = int.MinValue;

        foreach (var item in scheduled)
        {
            // A gap with nothing spanning it ends the cluster.
            if (item.StartMinutes!.Value >= clusterEnd && cluster.Count > 0)
            {
                Flush(cluster, columnEnds.Count, result);
                cluster.Clear();
                columnEnds.Clear();
                clusterEnd = int.MinValue;
            }

            var column = columnEnds.FindIndex(end => end <= item.StartMinutes!.Value);
            if (column < 0)
            {
                column = columnEnds.Count;
                columnEnds.Add(item.EndMinutes);
            }
            else
            {
                columnEnds[column] = item.EndMinutes;
            }

            cluster.Add((item, column));
            clusterEnd = Math.Max(clusterEnd, item.EndMinutes);
        }

        if (cluster.Count > 0) Flush(cluster, columnEnds.Count, result);

        return result;
    }

    private static void Flush(List<(ItineraryItem Item, int Column)> cluster, int columnCount, List<PositionedItem> into)
    {
        foreach (var (item, column) in cluster)
            into.Add(new PositionedItem(item, column, Math.Max(1, columnCount)));
    }
}
