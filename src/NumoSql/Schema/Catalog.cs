using NumoSql.Storage;

namespace NumoSql.Schema;

/// <summary>
/// The tables of a database. Changed only while the write gate is held, so a session that holds the gate always
/// sees a consistent set of tables.
/// </summary>
internal sealed class Catalog
{
    private readonly Dictionary<string, Table> tables = new(StringComparer.Ordinal);

    public IEnumerable<Table> Tables => tables.Values;

    public IEnumerable<string> TableNames => tables.Keys;

    public Table? Find(string tableName) => tables.GetValueOrDefault(tableName);

    /// <summary>Adds a table whose name is not taken. Callers check the name first.</summary>
    public void Add(Table table)
    {
        tables.Add(table.Definition.Name, table);
    }

    /// <summary>Removes and returns the table, or returns null when no table has that name.</summary>
    public Table? Remove(string tableName)
    {
        tables.Remove(tableName, out var removed);
        return removed;
    }
}
