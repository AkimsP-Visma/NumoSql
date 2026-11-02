using NumoSql.Types;

namespace NumoSql.Execution;

internal sealed class CountAccumulator : IAggregateAccumulator
{
    private long count;

    public void Add(object? value)
    {
        if (value is not null)
        {
            count++;
        }
    }

    public object? Result() => count;
}

/// <summary>Keeps an integer total exact until a real value appears, then continues in real arithmetic.</summary>
internal sealed class SumAccumulator : IAggregateAccumulator
{
    private long integerTotal;
    private double realTotal;
    private bool hasRealValue;
    private bool hasValue;

    public void Add(object? value)
    {
        if (value is null)
        {
            return;
        }

        AggregateInput.RequireNumeric("sum", value);
        hasValue = true;

        if (value is long integer && !hasRealValue)
        {
            integerTotal = AddIntegers(integerTotal, integer);
            return;
        }

        if (!hasRealValue)
        {
            realTotal = integerTotal;
            hasRealValue = true;
        }

        realTotal += ValueOrdering.AsReal(value);
        if (!double.IsFinite(realTotal))
        {
            throw new NumoSqlException("real value out of range");
        }
    }

    public object? Result()
    {
        if (!hasValue)
        {
            return null;
        }

        if (hasRealValue)
        {
            return realTotal;
        }

        return integerTotal;
    }

    private static long AddIntegers(long total, long integer)
    {
        try
        {
            return checked(total + integer);
        }
        catch (OverflowException overflow)
        {
            throw new NumoSqlException("integer out of range", overflow);
        }
    }
}

/// <summary>AVG returns a real. Inputs are summed as reals, so very large integer sums lose precision.</summary>
internal sealed class AverageAccumulator : IAggregateAccumulator
{
    private double total;
    private long count;

    public void Add(object? value)
    {
        if (value is null)
        {
            return;
        }

        AggregateInput.RequireNumeric("avg", value);
        total += ValueOrdering.AsReal(value);
        if (!double.IsFinite(total))
        {
            throw new NumoSqlException("real value out of range");
        }

        count++;
    }

    public object? Result()
    {
        if (count == 0)
        {
            return null;
        }

        return total / count;
    }
}

/// <summary>MIN and MAX over any type that orders with itself, such as text or integers.</summary>
internal sealed class ExtremumAccumulator : IAggregateAccumulator
{
    private readonly bool isMinimum;
    private object? best;

    public ExtremumAccumulator(bool isMinimum)
    {
        this.isMinimum = isMinimum;
    }

    public void Add(object? value)
    {
        if (value is null)
        {
            return;
        }

        if (best is null)
        {
            best = value;
            return;
        }

        var comparison = ValueOrdering.Compare(value, best);
        if (isMinimum ? comparison < 0 : comparison > 0)
        {
            best = value;
        }
    }

    public object? Result() => best;
}

internal static class AggregateInput
{
    public static void RequireNumeric(string functionName, object value)
    {
        if (!ValueOrdering.IsNumeric(value))
        {
            throw new NumoSqlException($"function {functionName}({ValueTypes.NameOf(value)}) does not exist");
        }
    }
}
