namespace NumoSql;

/// <summary>A problem the caller can act on: invalid SQL, a constraint violation or a type error.</summary>
public class NumoSqlException : Exception
{
    public NumoSqlException(string message)
        : base(message)
    {
    }

    public NumoSqlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
