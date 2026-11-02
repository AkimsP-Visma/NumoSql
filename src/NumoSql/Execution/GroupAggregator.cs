namespace NumoSql.Execution;

internal static class GroupAggregator
{
    /// <summary>
    /// Feeds every row into its group. Without GROUP BY the whole input is one group, which exists even when
    /// there are no rows, so COUNT(*) over an empty table yields 0.
    /// </summary>
    public static List<AggregateGroup> Aggregate(IEnumerable<object?[]> rows, GroupingPlan plan)
    {
        if (plan.KeyColumnIndexes.Count == 0)
        {
            var onlyGroup = plan.CreateGroup(Array.Empty<object?>());
            foreach (var row in rows)
            {
                Accumulate(onlyGroup, row, plan.Aggregates);
            }

            return [onlyGroup];
        }

        var groupsByKey = new Dictionary<object?[], AggregateGroup>(GroupKeyComparer.Instance);
        var groups = new List<AggregateGroup>();
        foreach (var row in rows)
        {
            var keys = plan.KeyColumnIndexes.Select(index => row[index]).ToArray();
            if (!groupsByKey.TryGetValue(keys, out var group))
            {
                group = plan.CreateGroup(keys);
                groupsByKey.Add(keys, group);
                groups.Add(group);
            }

            Accumulate(group, row, plan.Aggregates);
        }

        return groups;
    }

    private static void Accumulate(AggregateGroup group, object?[] row, IReadOnlyList<AggregateSpecification> aggregates)
    {
        for (var index = 0; index < aggregates.Count; index++)
        {
            group.Accumulators[index].Add(aggregates[index].Argument(row));
        }
    }
}
