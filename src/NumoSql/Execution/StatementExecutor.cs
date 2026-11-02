using NumoSql.Syntax;

namespace NumoSql.Execution;

/// <summary>Runs data and schema statements. Transaction control and CHECKPOINT are handled by the session.</summary>
internal static class StatementExecutor
{
    public static QueryResult Execute(Statement statement, StatementContext context) => statement switch
    {
        CreateTableStatement create => SchemaExecutor.CreateTable(create, context),
        DropTableStatement drop => SchemaExecutor.DropTable(drop, context),
        ShowTablesStatement => SchemaExecutor.ShowTables(context),
        InsertStatement insert => DataModification.Insert(insert, context),
        UpdateStatement update => DataModification.Update(update, context),
        DeleteStatement delete => DataModification.Delete(delete, context),
        SelectStatement select => SelectExecutor.Execute(select, context),
        _ => throw new InvalidOperationException($"{statement.GetType().Name} is not run by the executor"),
    };
}
