using System.Globalization;

namespace NumoSql.Types;

internal static class ValueFormatter
{
    /// <summary>Round-trip format for reals, so a value printed here reads back as the same number.</summary>
    private const string RealFormat = "R";

    public static string Format(object? value) => value switch
    {
        null => "NULL",
        bool boolean => boolean ? "true" : "false",
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString(RealFormat, CultureInfo.InvariantCulture),
        string text => text,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "unsupported value type"),
    };
}
