using System.Globalization;
using System.Text;
using NumoSql;

internal static class ResultFormatter
{
    private const string NullText = "NULL";
    private const string RealFormat = "R";
    private const string CellSeparator = " | ";
    private const string RuleSeparator = "-+-";
    private const char RuleCharacter = '-';

    public static string Format(QueryResult result)
    {
        var builder = new StringBuilder();
        if (result.ColumnNames.Count > 0)
        {
            AppendTable(builder, result);
        }

        builder.AppendLine(result.CommandTag);
        return builder.ToString();
    }

    private static void AppendTable(StringBuilder builder, QueryResult result)
    {
        var cells = result.Rows.Select(row => row.Select(FormatValue).ToArray()).ToList();

        var widths = result.ColumnNames.Select(name => name.Length).ToArray();
        foreach (var row in cells)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = Math.Max(widths[index], row[index].Length);
            }
        }

        AppendRow(builder, result.ColumnNames.ToArray(), widths);
        builder.AppendLine(string.Join(
            RuleSeparator,
            widths.Select(width => new string(RuleCharacter, width))));

        foreach (var row in cells)
        {
            AppendRow(builder, row, widths);
        }
    }

    private static void AppendRow(StringBuilder builder, string[] cells, int[] widths)
    {
        var padded = cells.Select((cell, index) => cell.PadRight(widths[index]));
        builder.AppendLine(string.Join(CellSeparator, padded).TrimEnd());
    }

    private static string FormatValue(object? value) => value switch
    {
        null => NullText,
        bool boolean => boolean ? "true" : "false",
        long integer => integer.ToString(CultureInfo.InvariantCulture),
        double real => real.ToString(RealFormat, CultureInfo.InvariantCulture),
        string text => text,
        _ => value.ToString() ?? string.Empty,
    };
}
