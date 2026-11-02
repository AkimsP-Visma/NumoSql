using NumoSql.Syntax;

namespace NumoSql.Execution;

/// <summary>
/// Turns an expression tree into delegates once, so per-row work is a chain of calls with no name lookups.
/// Name and function resolution is delegated to the leaves, which lets plain rows and aggregate groups share
/// the operator logic.
/// </summary>
internal static class ExpressionCompiler
{
    public static Func<TContext, object?> Compile<TContext>(Expression expression, IExpressionLeaves<TContext> leaves) =>
        expression switch
        {
            LiteralExpression literal => _ => literal.Value,
            ColumnReference column => leaves.ResolveColumn(column),
            FunctionCall call => leaves.ResolveFunction(call),
            UnaryExpression unary => CompileUnary(unary, leaves),
            BinaryExpression binary => CompileBinary(binary, leaves),
            IsNullExpression isNull => CompileIsNull(isNull, leaves),
            WildcardExpression => throw new NumoSqlException("* is only allowed in the select list"),
            _ => throw new InvalidOperationException($"unsupported expression {expression.GetType().Name}"),
        };

    public static Func<TContext, bool> CompileCondition<TContext>(
        Expression? expression,
        IExpressionLeaves<TContext> leaves,
        string clauseName)
    {
        if (expression is null)
        {
            return _ => true;
        }

        var value = Compile(expression, leaves);
        return context => ScalarOperators.IsTrue(value(context), clauseName);
    }

    private static Func<TContext, object?> CompileUnary<TContext>(
        UnaryExpression unary,
        IExpressionLeaves<TContext> leaves)
    {
        var operand = Compile(unary.Operand, leaves);
        return unary.Operator switch
        {
            UnaryOperator.Not => context => ScalarOperators.Not(operand(context)),
            UnaryOperator.Negate => context => ScalarOperators.Negate(operand(context)),
            UnaryOperator.Plus => context => ScalarOperators.Plus(operand(context)),
            _ => throw new InvalidOperationException($"unsupported unary operator {unary.Operator}"),
        };
    }

    private static Func<TContext, object?> CompileBinary<TContext>(
        BinaryExpression binary,
        IExpressionLeaves<TContext> leaves)
    {
        var left = Compile(binary.Left, leaves);
        var right = Compile(binary.Right, leaves);

        // AND and OR skip the right side when the left side already decides the result. The casts keep the
        // switch arms typed as object, so a boolean never gets widened.
        return binary.Operator switch
        {
            BinaryOperator.And => context =>
            {
                var leftValue = left(context);
                return leftValue is false ? (object?)false : ScalarOperators.And(leftValue, right(context));
            },
            BinaryOperator.Or => context =>
            {
                var leftValue = left(context);
                return leftValue is true ? (object?)true : ScalarOperators.Or(leftValue, right(context));
            },
            _ => CompileStrict(binary.Operator, left, right),
        };
    }

    private static Func<TContext, object?> CompileStrict<TContext>(
        BinaryOperator binaryOperator,
        Func<TContext, object?> left,
        Func<TContext, object?> right)
    {
        var operation = StrictOperation(binaryOperator);
        return context => operation(left(context), right(context));
    }

    private static Func<object?, object?, object?> StrictOperation(BinaryOperator binaryOperator) => binaryOperator switch
    {
        BinaryOperator.Equal => ScalarOperators.Equal,
        BinaryOperator.NotEqual => ScalarOperators.NotEqual,
        BinaryOperator.Less => ScalarOperators.Less,
        BinaryOperator.LessOrEqual => ScalarOperators.LessOrEqual,
        BinaryOperator.Greater => ScalarOperators.Greater,
        BinaryOperator.GreaterOrEqual => ScalarOperators.GreaterOrEqual,
        BinaryOperator.Concatenate => ScalarOperators.Concatenate,
        BinaryOperator.Add => ScalarOperators.Add,
        BinaryOperator.Subtract => ScalarOperators.Subtract,
        BinaryOperator.Multiply => ScalarOperators.Multiply,
        BinaryOperator.Divide => ScalarOperators.Divide,
        _ => throw new InvalidOperationException($"unsupported binary operator {binaryOperator}"),
    };

    private static Func<TContext, object?> CompileIsNull<TContext>(
        IsNullExpression isNull,
        IExpressionLeaves<TContext> leaves)
    {
        var operand = Compile(isNull.Operand, leaves);
        return context => (operand(context) is null) != isNull.IsNegated;
    }
}
