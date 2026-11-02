namespace NumoSql.Execution;

/// <param name="Keys">GROUP BY values for this group. Never modified, because the array is also a dictionary key.</param>
/// <param name="Accumulators">One accumulator per aggregate call, in registration order.</param>
internal sealed record AggregateGroup(object?[] Keys, IAggregateAccumulator[] Accumulators);
