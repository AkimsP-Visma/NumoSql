using NumoSql.Schema;

namespace NumoSql.Execution;

/// <summary>Names the columns of a row so that references resolve to positions.</summary>
internal sealed class RowScope
{
    private readonly IReadOnlyList<ScopeColumn> columns;

    public RowScope(IReadOnlyList<ScopeColumn> columns)
    {
        this.columns = columns;
    }

    public static RowScope Empty { get; } = new([]);

    public IReadOnlyList<ScopeColumn> Columns => columns;

    public int Count => columns.Count;

    public static RowScope ForTable(string sourceName, TableDefinition definition) =>
        new(definition.Columns.Select(column => new ScopeColumn(sourceName, column.Name)).ToList());

    public bool HasSource(string sourceName) => columns.Any(column => column.SourceName == sourceName);

    public RowScope Append(RowScope other) => new(columns.Concat(other.columns).ToList());

    /// <summary>Returns the position of the column. Fails when no column or more than one column matches.</summary>
    public int Resolve(string? qualifier, string columnName)
    {
        var matchIndex = -1;
        var matchCount = 0;

        for (var index = 0; index < columns.Count; index++)
        {
            var column = columns[index];
            if (column.ColumnName != columnName || (qualifier is not null && column.SourceName != qualifier))
            {
                continue;
            }

            matchIndex = index;
            matchCount++;
        }

        if (matchCount == 0)
        {
            throw new NumoSqlException($"column {DescribeReference(qualifier, columnName)} does not exist");
        }

        if (matchCount > 1)
        {
            throw new NumoSqlException($"column reference {DescribeReference(qualifier, columnName)} is ambiguous");
        }

        return matchIndex;
    }

    private static string DescribeReference(string? qualifier, string columnName) =>
        qualifier is null ? $"\"{columnName}\"" : $"\"{qualifier}.{columnName}\"";
}
