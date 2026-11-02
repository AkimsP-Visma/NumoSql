using NumoSql.Schema;

namespace NumoSql.Types;

internal static class ValueConversion
{
    /// <summary>
    /// Converts a value to the column's storage form. The only implicit conversion is integer to real,
    /// matching what a column of type REAL can hold without changing the stored meaning.
    /// </summary>
    public static object? ToColumnValue(object? value, ColumnDefinition column)
    {
        if (value is null)
        {
            if (column.IsNotNull)
            {
                throw new NumoSqlException($"null value in column \"{column.Name}\" violates not-null constraint");
            }

            return null;
        }

        return (column.Type, value) switch
        {
            (SqlType.Integer, long) => value,
            (SqlType.Real, double) => value,
            (SqlType.Real, long integer) => (double)integer,
            (SqlType.Text, string) => value,
            (SqlType.Boolean, bool) => value,
            _ => throw new NumoSqlException(
                $"column \"{column.Name}\" is of type {ValueTypes.NameOf(column.Type)} but the expression is of type {ValueTypes.NameOf(value)}"),
        };
    }
}
