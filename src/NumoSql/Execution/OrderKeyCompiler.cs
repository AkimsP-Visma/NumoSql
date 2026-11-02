using NumoSql.Syntax;

namespace NumoSql.Execution;

internal static class OrderKeyCompiler
{
    public static (IReadOnlyList<Func<TContext, object?>> Keys, IReadOnlyList<bool> Descending) Compile<TContext>(
        SelectStatement select,
        IExpressionLeaves<TContext> leaves)
    {
        var keys = new List<Func<TContext, object?>>();
        var descending = new List<bool>();
        foreach (var orderItem in select.OrderBy)
        {
            var value = ResolveAlias(orderItem.Value, select.Items);
            keys.Add(ExpressionCompiler.Compile(value, leaves));
            descending.Add(orderItem.IsDescending);
        }

        return (keys, descending);
    }

    /// <summary>A bare name that matches one output column, by alias or by its own name, sorts by that column, as in PostgreSQL.</summary>
    private static Expression ResolveAlias(Expression value, IReadOnlyList<SelectItem> items)
    {
        if (value is not ColumnReference { Qualifier: null } column)
        {
            return value;
        }

        var matches = items
            .Where(item => item.Value is not WildcardExpression && OutputPlanBuilder.OutputName(item) == column.Name)
            .ToList();
        if (matches.Count > 1)
        {
            throw new NumoSqlException($"ORDER BY \"{column.Name}\" is ambiguous");
        }

        return matches.Count == 1 ? matches[0].Value : value;
    }
}
