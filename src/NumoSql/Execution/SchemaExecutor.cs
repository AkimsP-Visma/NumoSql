using NumoSql.Schema;
using NumoSql.Storage;
using NumoSql.Syntax;

namespace NumoSql.Execution;

internal static class SchemaExecutor
{
    private const string CreateTableTag = "CREATE TABLE";
    private const string DropTableTag = "DROP TABLE";
    private const string TableNameColumn = "table_name";

    public static QueryResult CreateTable(CreateTableStatement statement, StatementContext context)
    {
        if (context.Catalog.Find(statement.TableName) is not null)
        {
            if (statement.IfNotExists)
            {
                return QueryResult.Command(CreateTableTag);
            }

            throw new NumoSqlException($"table \"{statement.TableName}\" already exists");
        }

        context.Catalog.Add(new Table(BuildDefinition(statement)));
        context.Transaction.RecordUndo(() => context.Catalog.Remove(statement.TableName));
        return QueryResult.Command(CreateTableTag);
    }

    public static QueryResult DropTable(DropTableStatement statement, StatementContext context)
    {
        var dropped = context.Catalog.Remove(statement.TableName);
        if (dropped is null)
        {
            if (statement.IfExists)
            {
                return QueryResult.Command(DropTableTag);
            }

            throw new NumoSqlException($"table \"{statement.TableName}\" does not exist");
        }

        context.Transaction.RecordUndo(() => context.Catalog.Add(dropped));
        return QueryResult.Command(DropTableTag);
    }

    public static QueryResult ShowTables(StatementContext context)
    {
        var rows = context.Catalog.TableNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new object?[] { name })
            .ToList();
        return QueryResult.ForRows([TableNameColumn], rows);
    }

    private static TableDefinition BuildDefinition(CreateTableStatement statement)
    {
        var columns = new List<ColumnDefinition>(statement.Columns.Count);
        var columnNames = new HashSet<string>(StringComparer.Ordinal);
        int? primaryKeyIndex = null;

        foreach (var specification in statement.Columns)
        {
            if (!columnNames.Add(specification.Name))
            {
                throw new NumoSqlException($"column \"{specification.Name}\" specified more than once");
            }

            if (specification.IsPrimaryKey)
            {
                primaryKeyIndex = columns.Count;
            }

            columns.Add(new ColumnDefinition(specification.Name, specification.Type, specification.IsNotNull));
        }

        return new TableDefinition(statement.TableName, columns, primaryKeyIndex);
    }
}
