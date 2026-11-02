namespace NumoSql;

/// <summary>
/// The outcome of one statement. Rows and ColumnNames are filled for SELECT and SHOW. RowsAffected counts the rows
/// a data-changing statement touched. CommandTag names the command, in the style PostgreSQL uses.
/// </summary>
public sealed record QueryResult(
    IReadOnlyList<string> ColumnNames,
    IReadOnlyList<object?[]> Rows,
    long RowsAffected,
    string CommandTag)
{
    internal static QueryResult ForRows(IReadOnlyList<string> columnNames, IReadOnlyList<object?[]> rows) =>
        new(columnNames, rows, rows.Count, $"SELECT {rows.Count}");

    internal static QueryResult Command(string commandTag, long rowsAffected = 0) => new([], [], rowsAffected, commandTag);
}
