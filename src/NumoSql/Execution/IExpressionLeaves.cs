using NumoSql.Syntax;

namespace NumoSql.Execution;

/// <summary>Decides how names and function calls evaluate in a context: a plain row, or an aggregate group.</summary>
internal interface IExpressionLeaves<TContext>
{
    Func<TContext, object?> ResolveColumn(ColumnReference column);

    Func<TContext, object?> ResolveFunction(FunctionCall call);
}
