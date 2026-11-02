using NumoSql.Syntax;

namespace NumoSql.Execution;

/// <summary>Resolves names against an input row. Aggregates are rejected here because no group exists.</summary>
internal sealed class RowLeaves : IExpressionLeaves<object?[]>
{
    private readonly RowScope scope;

    public RowLeaves(RowScope scope)
    {
        this.scope = scope;
    }

    public Func<object?[], object?> ResolveColumn(ColumnReference column)
    {
        var index = scope.Resolve(column.Qualifier, column.Name);
        return row => row[index];
    }

    public Func<object?[], object?> ResolveFunction(FunctionCall call) => throw new NumoSqlException(
        AggregateFunctions.IsAggregate(call.Name)
            ? $"aggregate function {call.Name}() is not allowed here"
            : $"function {call.Name}() does not exist");
}
