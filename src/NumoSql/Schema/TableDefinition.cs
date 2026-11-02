namespace NumoSql.Schema;

internal sealed record TableDefinition(string Name, IReadOnlyList<ColumnDefinition> Columns, int? PrimaryKeyIndex)
{
    private const int ColumnNotFound = -1;

    public int IndexOfColumn(string columnName)
    {
        for (var index = 0; index < Columns.Count; index++)
        {
            if (Columns[index].Name == columnName)
            {
                return index;
            }
        }

        return ColumnNotFound;
    }
}
