using NumoSql.Types;

namespace NumoSql.Syntax;

internal abstract record Statement
{
    /// <summary>Read-only statements are not written to the log and never create a commit.</summary>
    public virtual bool IsReadOnly => false;
}

internal sealed record CreateTableStatement(
    string TableName,
    bool IfNotExists,
    IReadOnlyList<ColumnSpecification> Columns) : Statement;

internal sealed record ColumnSpecification(string Name, SqlType Type, bool IsNotNull, bool IsPrimaryKey);

internal sealed record DropTableStatement(string TableName, bool IfExists) : Statement;

/// <summary>ColumnNames is null when the statement omits the column list and every column is targeted in order.</summary>
internal sealed record InsertStatement(
    string TableName,
    IReadOnlyList<string>? ColumnNames,
    IReadOnlyList<IReadOnlyList<Expression>> Rows) : Statement;

internal sealed record UpdateStatement(
    string TableName,
    IReadOnlyList<Assignment> Assignments,
    Expression? Condition) : Statement;

internal sealed record Assignment(string ColumnName, Expression Value);

internal sealed record DeleteStatement(string TableName, Expression? Condition) : Statement;

internal sealed record SelectStatement(
    IReadOnlyList<SelectItem> Items,
    TableReference? From,
    IReadOnlyList<JoinClause> Joins,
    Expression? Condition,
    IReadOnlyList<Expression> GroupBy,
    IReadOnlyList<OrderItem> OrderBy,
    long? Limit,
    long? Offset) : Statement
{
    public override bool IsReadOnly => true;
}

internal sealed record SelectItem(Expression Value, string? Alias);

internal sealed record TableReference(string Name, string? Alias)
{
    /// <summary>The name that qualifies this table's columns: the alias when one is given, else the table name.</summary>
    public string EffectiveName => Alias ?? Name;
}

internal sealed record JoinClause(TableReference Table, Expression Condition);

internal sealed record OrderItem(Expression Value, bool IsDescending);

/// <summary>NUMOSQL extension: lists the tables in the database.</summary>
internal sealed record ShowTablesStatement : Statement
{
    public override bool IsReadOnly => true;
}

/// <summary>NUMOSQL extension: writes a checkpoint and empties the write-ahead log.</summary>
internal sealed record CheckpointStatement : Statement;

internal sealed record BeginStatement : Statement;

internal sealed record CommitStatement : Statement;

internal sealed record RollbackStatement : Statement;
