namespace NumoSql.Execution;

internal interface IAggregateAccumulator
{
    /// <summary>Feeds one input value. Null inputs are ignored, which is how SQL aggregates treat NULL.</summary>
    void Add(object? value);

    object? Result();
}
