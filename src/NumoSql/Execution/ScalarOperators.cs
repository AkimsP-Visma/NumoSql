using NumoSql.Types;

namespace NumoSql.Execution;

/// <summary>
/// SQL operator semantics on runtime values. NULL propagates through arithmetic, comparison and concatenation.
/// AND, OR and NOT follow three-valued logic instead.
/// </summary>
internal static class ScalarOperators
{
    private const string AddSymbol = "+";
    private const string SubtractSymbol = "-";
    private const string MultiplySymbol = "*";
    private const string DivideSymbol = "/";
    private const string ConcatenateSymbol = "||";

    public static object? Add(object? left, object? right) =>
        Arithmetic(left, right, AddSymbol, (a, b) => checked(a + b), (a, b) => a + b);

    public static object? Subtract(object? left, object? right) =>
        Arithmetic(left, right, SubtractSymbol, (a, b) => checked(a - b), (a, b) => a - b);

    public static object? Multiply(object? left, object? right) =>
        Arithmetic(left, right, MultiplySymbol, (a, b) => checked(a * b), (a, b) => a * b);

    public static object? Divide(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (IsZero(right))
        {
            throw new NumoSqlException("division by zero");
        }

        return Arithmetic(left, right, DivideSymbol, (a, b) => checked(a / b), (a, b) => a / b);
    }

    public static object? Negate(object? operand)
    {
        switch (operand)
        {
            case null:
                return null;
            case long integer when integer == long.MinValue:
                throw new NumoSqlException("integer out of range");
            case long integer:
                return -integer;
            case double real:
                return -real;
            default:
                throw new NumoSqlException($"operator does not exist: -{ValueTypes.NameOf(operand)}");
        }
    }

    public static object? Plus(object? operand)
    {
        if (operand is null || ValueOrdering.IsNumeric(operand))
        {
            return operand;
        }

        throw new NumoSqlException($"operator does not exist: +{ValueTypes.NameOf(operand)}");
    }

    public static object? Concatenate(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (left is string leftText && right is string rightText)
        {
            return leftText + rightText;
        }

        throw OperatorError(ConcatenateSymbol, left, right);
    }

    public static object? Equal(object? left, object? right) => CompareWith(left, right, comparison => comparison == 0);

    public static object? NotEqual(object? left, object? right) => CompareWith(left, right, comparison => comparison != 0);

    public static object? Less(object? left, object? right) => CompareWith(left, right, comparison => comparison < 0);

    public static object? LessOrEqual(object? left, object? right) => CompareWith(left, right, comparison => comparison <= 0);

    public static object? Greater(object? left, object? right) => CompareWith(left, right, comparison => comparison > 0);

    public static object? GreaterOrEqual(object? left, object? right) => CompareWith(left, right, comparison => comparison >= 0);

    public static object? And(object? left, object? right)
    {
        RequireBooleanOrNull(left, "AND");
        RequireBooleanOrNull(right, "AND");

        if (left is false || right is false)
        {
            return false;
        }

        if (left is null || right is null)
        {
            return null;
        }

        return true;
    }

    public static object? Or(object? left, object? right)
    {
        RequireBooleanOrNull(left, "OR");
        RequireBooleanOrNull(right, "OR");

        if (left is true || right is true)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return null;
        }

        return false;
    }

    public static object? Not(object? operand)
    {
        RequireBooleanOrNull(operand, "NOT");
        if (operand is null)
        {
            return null;
        }

        return !(bool)operand;
    }

    /// <summary>A WHERE, ON or similar clause keeps a row only when its condition is exactly TRUE.</summary>
    public static bool IsTrue(object? value, string clauseName) => value switch
    {
        null => false,
        bool boolean => boolean,
        _ => throw new NumoSqlException(
            $"argument of {clauseName} must be type boolean, not type {ValueTypes.NameOf(value)}"),
    };

    private static object? Arithmetic(
        object? left,
        object? right,
        string symbol,
        Func<long, long, long> integerOperation,
        Func<double, double, double> realOperation)
    {
        if (left is null || right is null)
        {
            return null;
        }

        if (!ValueOrdering.IsNumeric(left) || !ValueOrdering.IsNumeric(right))
        {
            throw OperatorError(symbol, left, right);
        }

        if (left is long leftInteger && right is long rightInteger)
        {
            try
            {
                return integerOperation(leftInteger, rightInteger);
            }
            catch (OverflowException overflow)
            {
                throw new NumoSqlException("integer out of range", overflow);
            }
        }

        var result = realOperation(ValueOrdering.AsReal(left), ValueOrdering.AsReal(right));
        if (!double.IsFinite(result))
        {
            throw new NumoSqlException("real value out of range");
        }

        return result;
    }

    private static object? CompareWith(object? left, object? right, Func<int, bool> accepts)
    {
        if (left is null || right is null)
        {
            return null;
        }

        return accepts(ValueOrdering.Compare(left, right));
    }

    private static bool IsZero(object value) => value switch
    {
        long integer => integer == 0,
        double real => real == 0,
        _ => false,
    };

    private static void RequireBooleanOrNull(object? value, string operatorName)
    {
        if (value is not (null or bool))
        {
            throw new NumoSqlException(
                $"argument of {operatorName} must be type boolean, not type {ValueTypes.NameOf(value)}");
        }
    }

    private static NumoSqlException OperatorError(string symbol, object? left, object? right) =>
        new($"operator does not exist: {ValueTypes.NameOf(left)} {symbol} {ValueTypes.NameOf(right)}");
}
