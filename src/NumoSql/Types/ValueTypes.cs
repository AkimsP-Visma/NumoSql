namespace NumoSql.Types;

internal static class ValueTypes
{
    public static string NameOf(object? value) => value switch
    {
        null => "null",
        long => "integer",
        double => "real",
        string => "text",
        bool => "boolean",
        _ => "unknown",
    };

    public static string NameOf(SqlType type) => type switch
    {
        SqlType.Integer => "integer",
        SqlType.Real => "real",
        SqlType.Text => "text",
        SqlType.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unknown SQL type"),
    };
}
