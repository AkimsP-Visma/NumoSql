using NumoSql.Schema;
using NumoSql.Storage;
using NumoSql.Syntax;
using NumoSql.Types;

namespace NumoSql.Execution;

/// <summary>Builds the input of a SELECT: the FROM table, then each INNER JOIN applied as a nested loop.</summary>
internal static class SourceBuilder
{
    private static readonly object?[] SingleEmptyRow = [];

    public static SourceRows Build(SelectStatement select, Catalog catalog)
    {
        // Without FROM the query has one implicit row, so that SELECT 1 + 1 yields a single row.
        if (select.From is null)
        {
            return new SourceRows(RowScope.Empty, [SingleEmptyRow]);
        }

        var fromTable = RequireTable(select.From.Name, catalog);
        var rows = PrimaryKeyRows(select, select.From, fromTable) ?? fromTable.Rows;
        var source = new SourceRows(RowScope.ForTable(select.From.EffectiveName, fromTable.Definition), rows);

        foreach (var join in select.Joins)
        {
            source = JoinRows(source, join, catalog);
        }

        return source;
    }

    /// <summary>
    /// A WHERE that compares the primary key column with a literal reads one row through the key index. The whole
    /// condition is still applied to that row, so the index only narrows the search. Returns null when no lookup applies.
    /// </summary>
    private static IReadOnlyList<object?[]>? PrimaryKeyRows(SelectStatement select, TableReference from, Table table)
    {
        if (select.Joins.Count > 0 || select.Condition is not BinaryExpression { Operator: BinaryOperator.Equal } equality)
        {
            return null;
        }

        if (table.Definition.PrimaryKeyIndex is not { } keyIndex)
        {
            return null;
        }

        var keyColumn = table.Definition.Columns[keyIndex];
        var (column, literal) = SplitEquality(equality);
        if (column is null || literal is null
            || column.Name != keyColumn.Name
            || (column.Qualifier is not null && column.Qualifier != from.EffectiveName))
        {
            return null;
        }

        var key = KeyFor(keyColumn.Type, literal.Value);
        if (key is null)
        {
            return null;
        }

        if (table.TryFindByPrimaryKey(key, out var row))
        {
            return [row!];
        }

        return [];
    }

    private static (ColumnReference? Column, LiteralExpression? Literal) SplitEquality(BinaryExpression equality) =>
        (equality.Left, equality.Right) switch
        {
            (ColumnReference column, LiteralExpression literal) => (column, literal),
            (LiteralExpression literal, ColumnReference column) => (column, literal),
            _ => (null, null),
        };

    /// <summary>
    /// The stored form of a literal used as a key of a column of this type. Null means the literal needs the general
    /// comparison, for example text compared with an integer column.
    /// </summary>
    private static object? KeyFor(SqlType columnType, object? value) => (columnType, value) switch
    {
        (SqlType.Integer, long integer) => integer,
        (SqlType.Real, double real) => real,
        (SqlType.Real, long integer) => (double)integer,
        (SqlType.Text, string text) => text,
        (SqlType.Boolean, bool flag) => flag,
        _ => null,
    };

    private static SourceRows JoinRows(SourceRows left, JoinClause join, Catalog catalog)
    {
        var joinedTable = RequireTable(join.Table.Name, catalog);
        var sourceName = join.Table.EffectiveName;
        if (left.Scope.HasSource(sourceName))
        {
            throw new NumoSqlException($"table name \"{sourceName}\" specified more than once");
        }

        var combinedScope = left.Scope.Append(RowScope.ForTable(sourceName, joinedTable.Definition));
        var condition = ExpressionCompiler.CompileCondition(join.Condition, new RowLeaves(combinedScope), "JOIN/ON");

        // One scratch row serves every candidate pair. Only the pairs that match are copied out.
        var scratch = new object?[combinedScope.Count];
        var combinedRows = new List<object?[]>();
        foreach (var leftRow in left.Rows)
        {
            foreach (var rightRow in joinedTable.Rows)
            {
                leftRow.CopyTo(scratch, 0);
                rightRow.CopyTo(scratch, leftRow.Length);
                if (condition(scratch))
                {
                    combinedRows.Add((object?[])scratch.Clone());
                }
            }
        }

        return new SourceRows(combinedScope, combinedRows);
    }

    private static Table RequireTable(string tableName, Catalog catalog) =>
        catalog.Find(tableName) ?? throw new NumoSqlException($"table \"{tableName}\" does not exist");
}
