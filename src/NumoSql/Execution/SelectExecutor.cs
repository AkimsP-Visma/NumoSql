using NumoSql.Syntax;

namespace NumoSql.Execution;

internal static class SelectExecutor
{
    public static QueryResult Execute(SelectStatement select, StatementContext context)
    {
        var source = SourceBuilder.Build(select, context.Catalog);
        var filteredRows = FilterRows(source, select.Condition);

        return IsGrouped(select)
            ? ExecuteGrouped(select, source.Scope, filteredRows)
            : ExecuteUngrouped(select, source.Scope, filteredRows);
    }

    private static IEnumerable<object?[]> FilterRows(SourceRows source, Expression? condition)
    {
        var predicate = ExpressionCompiler.CompileCondition(condition, new RowLeaves(source.Scope), "WHERE");
        return source.Rows.Where(predicate);
    }

    private static bool IsGrouped(SelectStatement select) =>
        select.GroupBy.Count > 0
        || select.Items.Any(item => ContainsAggregate(item.Value))
        || select.OrderBy.Any(item => ContainsAggregate(item.Value));

    private static bool ContainsAggregate(Expression expression) =>
        ExpressionTree.Any(expression, node => node is FunctionCall call && AggregateFunctions.IsAggregate(call.Name));

    private static QueryResult ExecuteUngrouped(SelectStatement select, RowScope scope, IEnumerable<object?[]> rows)
    {
        var plan = OutputPlanBuilder.Build(select, new RowLeaves(scope), () => ExpandWildcard(scope));
        return OutputAssembler.Assemble(rows, plan, select.Limit, select.Offset);
    }

    private static QueryResult ExecuteGrouped(SelectStatement select, RowScope scope, IEnumerable<object?[]> rows)
    {
        var grouping = new GroupingPlan(scope, select.GroupBy);

        // The plan compiles first because it registers the aggregates that GroupAggregator must feed.
        var plan = OutputPlanBuilder.Build<AggregateGroup>(
            select,
            grouping,
            () => throw new NumoSqlException("SELECT * is not allowed with GROUP BY or aggregate functions"));

        var groups = GroupAggregator.Aggregate(rows, grouping);
        return OutputAssembler.Assemble(groups, plan, select.Limit, select.Offset);
    }

    private static IEnumerable<NamedProjection<object?[]>> ExpandWildcard(RowScope scope)
    {
        if (scope.Count == 0)
        {
            throw new NumoSqlException("SELECT * with no tables specified is not valid");
        }

        for (var index = 0; index < scope.Count; index++)
        {
            var position = index;
            yield return new NamedProjection<object?[]>(scope.Columns[position].ColumnName, row => row[position]);
        }
    }
}
