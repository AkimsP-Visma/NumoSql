namespace NumoSql.Execution;

/// <summary>Projects, sorts and pages the contexts of a SELECT into its result.</summary>
internal static class OutputAssembler
{
    public static QueryResult Assemble<TContext>(
        IEnumerable<TContext> contexts,
        OutputPlan<TContext> plan,
        long? limit,
        long? offset)
    {
        var rows = plan.SortKeys.Count == 0
            ? ProjectPage(contexts, plan, limit, offset)
            : ProjectSortedPage(contexts, plan, limit, offset);
        return QueryResult.ForRows(plan.ColumnNames, rows);
    }

    /// <summary>
    /// Without ORDER BY the page is taken lazily. Rows past the page are never projected, so an error in them is not
    /// raised, and LIMIT stops the work early.
    /// </summary>
    private static List<object?[]> ProjectPage<TContext>(
        IEnumerable<TContext> contexts,
        OutputPlan<TContext> plan,
        long? limit,
        long? offset)
    {
        var rows = new List<object?[]>();
        var rowsToSkip = offset ?? 0;

        using var enumerator = contexts.GetEnumerator();
        while ((limit is null || rows.Count < limit.Value) && enumerator.MoveNext())
        {
            if (rowsToSkip > 0)
            {
                rowsToSkip--;
                continue;
            }

            rows.Add(Project(plan.Projections, enumerator.Current));
        }

        return rows;
    }

    /// <summary>Sorting needs every sort key, but only the rows that survive paging are projected.</summary>
    private static List<object?[]> ProjectSortedPage<TContext>(
        IEnumerable<TContext> contexts,
        OutputPlan<TContext> plan,
        long? limit,
        long? offset)
    {
        var keyed = contexts
            .Select(context => new KeyedContext<TContext>(context, Project(plan.SortKeys, context)))
            .ToList();
        var sorted = keyed.OrderBy(item => item.SortKeys, new SortKeyComparer(plan.SortDescending)).ToList();

        var skipped = (int)Math.Min(offset ?? 0, sorted.Count);
        var available = sorted.Count - skipped;
        var taken = limit is null ? available : (int)Math.Min(limit.Value, available);

        return sorted
            .GetRange(skipped, taken)
            .Select(item => Project(plan.Projections, item.Context))
            .ToList();
    }

    private static object?[] Project<TContext>(IReadOnlyList<Func<TContext, object?>> values, TContext context)
    {
        var output = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            output[index] = values[index](context);
        }

        return output;
    }

    private sealed record KeyedContext<TContext>(TContext Context, object?[] SortKeys);
}
