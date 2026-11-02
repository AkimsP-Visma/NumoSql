namespace NumoSql;

public sealed class SqlSyntaxException : NumoSqlException
{
    public SqlSyntaxException(string message, int position)
        : base($"syntax error: {message} (at character {position + 1})")
    {
        Position = position;
    }

    /// <summary>Zero-based offset of the offending character in the SQL text.</summary>
    public int Position { get; }
}
