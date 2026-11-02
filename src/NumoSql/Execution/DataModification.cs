using NumoSql.Schema;
using NumoSql.Storage;
using NumoSql.Syntax;
using NumoSql.Types;

namespace NumoSql.Execution;

/// <summary>
/// INSERT, UPDATE and DELETE. Each one builds its full set of new rows before the table changes, so a failing
/// row leaves the table exactly as it was. Every change registers its undo action with the transaction.
/// </summary>
internal static class DataModification
{
    public static QueryResult Insert(InsertStatement statement, StatementContext context)
    {
        var table = RequireTable(statement.TableName, context.Catalog);
        var targetIndexes = ResolveTargetColumns(table.Definition, statement.ColumnNames);
        var leaves = new RowLeaves(RowScope.Empty);

        var newRows = statement.Rows
            .Select(valueRow => BuildRow(
                valueRow.Select(value => ExpressionCompiler.Compile(value, leaves)).ToList(),
                targetIndexes,
                table.Definition))
            .ToList();

        context.Transaction.RecordUndo(table.AppendRows(newRows));
        return QueryResult.Command($"INSERT 0 {newRows.Count}", newRows.Count);
    }

    public static QueryResult Update(UpdateStatement statement, StatementContext context)
    {
        var table = RequireTable(statement.TableName, context.Catalog);
        var leaves = new RowLeaves(RowScope.ForTable(statement.TableName, table.Definition));
        var condition = ExpressionCompiler.CompileCondition(statement.Condition, leaves, "WHERE");
        var assignments = CompileAssignments(statement.Assignments, table.Definition, leaves);

        var updatedRows = new List<object?[]>(table.Rows.Count);
        long updatedCount = 0;
        foreach (var row in table.Rows)
        {
            if (condition(row))
            {
                updatedRows.Add(ApplyAssignments(row, assignments));
                updatedCount++;
            }
            else
            {
                updatedRows.Add(row);
            }
        }

        context.Transaction.RecordUndo(table.ReplaceRows(updatedRows));
        return QueryResult.Command($"UPDATE {updatedCount}", updatedCount);
    }

    public static QueryResult Delete(DeleteStatement statement, StatementContext context)
    {
        var table = RequireTable(statement.TableName, context.Catalog);
        var leaves = new RowLeaves(RowScope.ForTable(statement.TableName, table.Definition));
        var condition = ExpressionCompiler.CompileCondition(statement.Condition, leaves, "WHERE");

        var keptRows = new List<object?[]>(table.Rows.Count);
        long deletedCount = 0;
        foreach (var row in table.Rows)
        {
            if (condition(row))
            {
                deletedCount++;
            }
            else
            {
                keptRows.Add(row);
            }
        }

        context.Transaction.RecordUndo(table.ReplaceRows(keptRows));
        return QueryResult.Command($"DELETE {deletedCount}", deletedCount);
    }

    private static Table RequireTable(string tableName, Catalog catalog) =>
        catalog.Find(tableName) ?? throw new NumoSqlException($"table \"{tableName}\" does not exist");

    private static IReadOnlyList<int> ResolveTargetColumns(TableDefinition definition, IReadOnlyList<string>? columnNames)
    {
        if (columnNames is null)
        {
            return Enumerable.Range(0, definition.Columns.Count).ToList();
        }

        var indexes = new List<int>(columnNames.Count);
        var seenIndexes = new HashSet<int>();
        foreach (var name in columnNames)
        {
            var index = definition.IndexOfColumn(name);
            if (index < 0)
            {
                throw new NumoSqlException($"column \"{name}\" of table \"{definition.Name}\" does not exist");
            }

            if (!seenIndexes.Add(index))
            {
                throw new NumoSqlException($"column \"{name}\" specified more than once");
            }

            indexes.Add(index);
        }

        return indexes;
    }

    private static object?[] BuildRow(
        IReadOnlyList<Func<object?[], object?>> values,
        IReadOnlyList<int> targetIndexes,
        TableDefinition definition)
    {
        if (values.Count != targetIndexes.Count)
        {
            throw new NumoSqlException(values.Count > targetIndexes.Count
                ? "INSERT has more expressions than target columns"
                : "INSERT has more target columns than expressions");
        }

        var row = new object?[definition.Columns.Count];
        var isAssigned = new bool[definition.Columns.Count];

        for (var index = 0; index < values.Count; index++)
        {
            var columnIndex = targetIndexes[index];
            var value = values[index](Array.Empty<object?>());
            row[columnIndex] = ValueConversion.ToColumnValue(value, definition.Columns[columnIndex]);
            isAssigned[columnIndex] = true;
        }

        // Columns that the statement does not name receive NULL, which fails for NOT NULL columns.
        for (var columnIndex = 0; columnIndex < row.Length; columnIndex++)
        {
            if (!isAssigned[columnIndex])
            {
                row[columnIndex] = ValueConversion.ToColumnValue(null, definition.Columns[columnIndex]);
            }
        }

        return row;
    }

    private static IReadOnlyList<CompiledAssignment> CompileAssignments(
        IReadOnlyList<Assignment> assignments,
        TableDefinition definition,
        RowLeaves leaves)
    {
        var compiled = new List<CompiledAssignment>(assignments.Count);
        var seenIndexes = new HashSet<int>();

        foreach (var assignment in assignments)
        {
            var index = definition.IndexOfColumn(assignment.ColumnName);
            if (index < 0)
            {
                throw new NumoSqlException($"column \"{assignment.ColumnName}\" of table \"{definition.Name}\" does not exist");
            }

            if (!seenIndexes.Add(index))
            {
                throw new NumoSqlException($"column \"{assignment.ColumnName}\" specified more than once");
            }

            var value = ExpressionCompiler.Compile(assignment.Value, leaves);
            compiled.Add(new CompiledAssignment(index, definition.Columns[index], value));
        }

        return compiled;
    }

    /// <summary>Every right-hand side reads the original row, so SET a = b, b = a swaps the two values.</summary>
    private static object?[] ApplyAssignments(object?[] row, IReadOnlyList<CompiledAssignment> assignments)
    {
        var updated = (object?[])row.Clone();
        foreach (var assignment in assignments)
        {
            updated[assignment.ColumnIndex] = ValueConversion.ToColumnValue(assignment.Value(row), assignment.Column);
        }

        return updated;
    }

    private sealed record CompiledAssignment(int ColumnIndex, ColumnDefinition Column, Func<object?[], object?> Value);
}
