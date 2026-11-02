namespace NumoSql.Execution;

/// <summary>A column in a row scope: the table or alias it came from, and its name.</summary>
internal sealed record ScopeColumn(string SourceName, string ColumnName);
