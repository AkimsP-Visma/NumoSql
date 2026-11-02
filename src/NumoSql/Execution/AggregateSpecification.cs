namespace NumoSql.Execution;

/// <param name="Argument">Evaluates the aggregate's input for one row. COUNT(*) uses a constant non-null input.</param>
/// <param name="CreateAccumulator">Starts an empty accumulator for a new group.</param>
internal sealed record AggregateSpecification(
    Func<object?[], object?> Argument,
    Func<IAggregateAccumulator> CreateAccumulator);
