namespace NumoSql.Execution;

/// <summary>The rows a SELECT reads, together with the scope that names their columns.</summary>
internal sealed record SourceRows(RowScope Scope, IReadOnlyList<object?[]> Rows);
