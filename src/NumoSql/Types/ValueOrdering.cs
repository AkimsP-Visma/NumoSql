namespace NumoSql.Types;

internal static class ValueOrdering
{
    /// <summary>Orders two non-null values. Integers and reals compare numerically; mixing other types is an error.</summary>
    public static int Compare(object left, object right)
    {
        if (left is long leftInteger && right is long rightInteger)
        {
            return leftInteger.CompareTo(rightInteger);
        }

        if (IsNumeric(left) && IsNumeric(right))
        {
            return AsReal(left).CompareTo(AsReal(right));
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        if (left is bool leftFlag && right is bool rightFlag)
        {
            return leftFlag.CompareTo(rightFlag);
        }

        throw new NumoSqlException(
            $"cannot compare values of type {ValueTypes.NameOf(left)} and {ValueTypes.NameOf(right)}");
    }

    public static bool IsNumeric(object value) => value is long or double;

    public static double AsReal(object numeric) => numeric is long integer ? integer : (double)numeric;
}
