using NumoSql.Schema;
using NumoSql.Types;

namespace NumoSql.Storage;

/// <summary>
/// Rows of one table, plus an index from primary key value to row when the table has a primary key. A row array is
/// never modified in place. A change either appends to the row list or swaps in a new list, and each change returns
/// the action that reverses it.
/// </summary>
internal sealed class Table
{
    private List<object?[]> rows;
    private Dictionary<object, object?[]>? rowsByPrimaryKey;

    public Table(TableDefinition definition)
        : this(definition, [], definition.PrimaryKeyIndex is null ? null : new Dictionary<object, object?[]>())
    {
    }

    private Table(TableDefinition definition, List<object?[]> rows, Dictionary<object, object?[]>? rowsByPrimaryKey)
    {
        Definition = definition;
        this.rows = rows;
        this.rowsByPrimaryKey = rowsByPrimaryKey;
    }

    public TableDefinition Definition { get; }

    public IReadOnlyList<object?[]> Rows => rows;

    /// <summary>Looks up a row by its primary key value. Returns false when there is no such row or no primary key.</summary>
    public bool TryFindByPrimaryKey(object key, out object?[]? row)
    {
        if (rowsByPrimaryKey is null)
        {
            row = null;
            return false;
        }

        return rowsByPrimaryKey.TryGetValue(key, out row);
    }

    /// <summary>Appends rows, or appends none: every key is checked before the table changes.</summary>
    public Action AppendRows(IReadOnlyList<object?[]> newRows)
    {
        var appendTarget = rows;
        var previousRowCount = appendTarget.Count;

        if (rowsByPrimaryKey is null)
        {
            appendTarget.AddRange(newRows);
            return () => appendTarget.RemoveRange(previousRowCount, newRows.Count);
        }

        var index = rowsByPrimaryKey;
        var incomingRows = new Dictionary<object, object?[]>();
        foreach (var row in newRows)
        {
            var key = PrimaryKeyOf(row);
            if (index.ContainsKey(key) || !incomingRows.TryAdd(key, row))
            {
                throw DuplicateKey(key);
            }
        }

        appendTarget.AddRange(newRows);
        foreach (var (key, row) in incomingRows)
        {
            index[key] = row;
        }

        return () =>
        {
            appendTarget.RemoveRange(previousRowCount, newRows.Count);
            foreach (var key in incomingRows.Keys)
            {
                index.Remove(key);
            }
        };
    }

    /// <summary>Replaces every row, or leaves the table unchanged if the new rows break the primary key.</summary>
    public Action ReplaceRows(List<object?[]> replacementRows)
    {
        var previousRows = rows;
        var previousIndex = rowsByPrimaryKey;

        Dictionary<object, object?[]>? replacementIndex = null;
        if (previousIndex is not null)
        {
            replacementIndex = new Dictionary<object, object?[]>();
            foreach (var row in replacementRows)
            {
                var key = PrimaryKeyOf(row);
                if (!replacementIndex.TryAdd(key, row))
                {
                    throw DuplicateKey(key);
                }
            }
        }

        rows = replacementRows;
        rowsByPrimaryKey = replacementIndex;
        return () =>
        {
            rows = previousRows;
            rowsByPrimaryKey = previousIndex;
        };
    }

    private object PrimaryKeyOf(object?[] row)
    {
        // The NOT NULL check on primary key columns runs before rows reach a table, so a null here is a bug.
        return row[Definition.PrimaryKeyIndex!.Value]
            ?? throw new InvalidOperationException($"primary key of table \"{Definition.Name}\" is null");
    }

    private NumoSqlException DuplicateKey(object key) => new(
        $"duplicate key value violates primary key constraint of table \"{Definition.Name}\": {ValueFormatter.Format(key)}");
}
