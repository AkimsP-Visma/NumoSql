using NumoSql.Syntax;

namespace NumoSql.Execution;

internal static class OutputPlanBuilder
{
    private const string UnnamedColumn = "?column?";

    /// <param name="expandWildcard">Produces the columns for a * item. It depends on whether rows or groups are projected.</param>
    public static OutputPlan<TContext> Build<TContext>(
        SelectStatement select,
        IExpressionLeaves<TContext> leaves,
        Func<IEnumerable<NamedProjection<TContext>>> expandWildcard)
    {
        var projections = new List<NamedProjection<TContext>>();
        foreach (var item in select.Items)
        {
            if (item.Value is WildcardExpression)
            {
                projections.AddRange(expandWildcard());
            }
            else
            {
                var value = ExpressionCompiler.Compile(item.Value, leaves);
                projections.Add(new NamedProjection<TContext>(OutputName(item), value));
            }
        }

        var (sortKeys, sortDescending) = OrderKeyCompiler.Compile(select, leaves);
        return new OutputPlan<TContext>(
            projections.Select(projection => projection.Name).ToList(),
            projections.Select(projection => projection.Value).ToList(),
            sortKeys,
            sortDescending);
    }

    internal static string OutputName(SelectItem item) => item.Alias ?? NameOfValue(item.Value);

    private static string NameOfValue(Expression value) => value switch
    {
        ColumnReference column => column.Name,
        FunctionCall call => call.Name,
        _ => UnnamedColumn,
    };
}
