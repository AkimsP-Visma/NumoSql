namespace NumoSql.Execution;

internal static class AggregateFunctions
{
    private static readonly IReadOnlyDictionary<string, Func<IAggregateAccumulator>> Factories =
        new Dictionary<string, Func<IAggregateAccumulator>>(StringComparer.Ordinal)
        {
            ["count"] = () => new CountAccumulator(),
            ["sum"] = () => new SumAccumulator(),
            ["avg"] = () => new AverageAccumulator(),
            ["min"] = () => new ExtremumAccumulator(isMinimum: true),
            ["max"] = () => new ExtremumAccumulator(isMinimum: false),
        };

    public static bool IsAggregate(string functionName) => Factories.ContainsKey(functionName);

    public static Func<IAggregateAccumulator> FactoryFor(string functionName) => Factories[functionName];
}
