namespace NumoSql;

/// <summary>
/// The database files fail a checksum or describe an impossible state. The database refuses to open
/// rather than guess at the contents.
/// </summary>
public sealed class DatabaseCorruptException : NumoSqlException
{
    public DatabaseCorruptException(string message)
        : base($"database is corrupt: {message}")
    {
    }

    public DatabaseCorruptException(string message, Exception innerException)
        : base($"database is corrupt: {message}", innerException)
    {
    }
}
