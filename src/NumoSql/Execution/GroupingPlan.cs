using NumoSql.Syntax;

namespace NumoSql.Execution;

/// <summary>
/// Resolves SELECT and ORDER BY expressions against groups. Each aggregate call registers itself while the
/// expressions compile, so every group gets one accumulator per call. All expressions must compile before any
/// group is created.
/// </summary>
internal sealed class GroupingPlan : IExpressionLeaves<AggregateGroup>
{
    private readonly RowScope inputScope;
    private readonly List<int> keyColumnIndexes;
    private readonly List<AggregateSpecification> aggregates = [];

    public GroupingPlan(RowScope inputScope, IReadOnlyList<Expression> groupBy)
    {
        this.inputScope = inputScope;
        keyColumnIndexes = groupBy.Select(ResolveKeyColumn).ToList();
    }

    public IReadOnlyList<int> KeyColumnIndexes => keyColumnIndexes;

    public IReadOnlyList<AggregateSpecification> Aggregates => aggregates;

    public AggregateGroup CreateGroup(object?[] keys) =>
        new(keys, aggregates.Select(aggregate => aggregate.CreateAccumulator()).ToArray());

    public Func<AggregateGroup, object?> ResolveColumn(ColumnReference column)
    {
        var inputIndex = inputScope.Resolve(column.Qualifier, column.Name);
        var keyPosition = keyColumnIndexes.IndexOf(inputIndex);
        if (keyPosition < 0)
        {
            throw new NumoSqlException(
                $"column \"{column.Name}\" must appear in the GROUP BY clause or be used in an aggregate function");
        }

        return group => group.Keys[keyPosition];
    }

    public Func<AggregateGroup, object?> ResolveFunction(FunctionCall call)
    {
        if (!AggregateFunctions.IsAggregate(call.Name))
        {
            throw new NumoSqlException($"function {call.Name}() does not exist");
        }

        var slot = RegisterAggregate(call);
        return group => group.Accumulators[slot].Result();
    }

    private int ResolveKeyColumn(Expression key)
    {
        if (key is not ColumnReference column)
        {
            throw new NumoSqlException("GROUP BY supports column references only");
        }

        return inputScope.Resolve(column.Qualifier, column.Name);
    }

    private int RegisterAggregate(FunctionCall call)
    {
        aggregates.Add(new AggregateSpecification(CompileArgument(call), AggregateFunctions.FactoryFor(call.Name)));
        return aggregates.Count - 1;
    }

    private Func<object?[], object?> CompileArgument(FunctionCall call)
    {
        if (call.IsWildcardArgument)
        {
            if (call.Name != "count")
            {
                throw new NumoSqlException($"{call.Name}(*) is not supported, only count(*) is");
            }

            return _ => true;
        }

        if (call.Arguments.Count != 1)
        {
            throw new NumoSqlException($"function {call.Name}() takes exactly one argument");
        }

        return ExpressionCompiler.Compile(call.Arguments[0], new RowLeaves(inputScope));
    }
}
