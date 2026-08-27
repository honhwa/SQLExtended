using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SQLExtended.Monitoring;

/// <summary>
/// Merges a freshly collected list into a bound <see cref="ObservableCollection{T}"/> by key, updating
/// matched rows in place instead of replacing the collection.
///
/// This exists for one reason: the grids refresh every few seconds, and swapping ItemsSource on each poll
/// throws away the user's selection and scroll position — which is intolerable when you are watching one
/// replica's queue climb. Matched rows get their values copied across (each row raises PropertyChanged),
/// new rows are inserted, and vanished rows are removed.
/// </summary>
internal static class RowMerge
{
    public static void Apply<T>(ObservableCollection<T> target, IList<T> fresh, Func<T, string> keyOf, Action<T, T> copyInto) where T : class
    {
        var freshByKey = new Dictionary<string, T>(fresh.Count, StringComparer.OrdinalIgnoreCase);
        var order = new List<string>(fresh.Count);

        foreach (var row in fresh)
        {
            string key = keyOf(row);
            if (freshByKey.ContainsKey(key)) continue; // first row wins; the queries order deterministically
            freshByKey[key] = row;
            order.Add(key);
        }

        // Remove rows that are gone, and update the ones that remain.
        var existingByKey = new Dictionary<string, T>(target.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = target.Count - 1; i >= 0; i--)
        {
            string key = keyOf(target[i]);
            if (!freshByKey.TryGetValue(key, out var updated))
            {
                target.RemoveAt(i);
                continue;
            }

            copyInto(target[i], updated);
            existingByKey[key] = target[i];
        }

        // Insert new rows at their collected position so the grid's default order matches the query's.
        for (int i = 0; i < order.Count; i++)
        {
            if (existingByKey.ContainsKey(order[i])) continue;

            var row = freshByKey[order[i]];
            int index = Math.Min(i, target.Count);
            target.Insert(index, row);
            existingByKey[order[i]] = row;
        }
    }
}
