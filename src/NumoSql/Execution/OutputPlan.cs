namespace NumoSql.Execution;

/// <summary>A projected output column: its name and how to compute it from one input context.</summary>
internal sealed record NamedProjection<TContext>(string Name, Func<TContext, object?> Value);

/// <summary>How a SELECT turns each input context into an output row, before ordering and paging.</summary>
internal sealed record OutputPlan<TContext>(
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<Func<TContext, object?>> Projections,
    IReadOnlyList<Func<TContext, object?>> SortKeys,
    IReadOnlyList<bool> SortDescending);
