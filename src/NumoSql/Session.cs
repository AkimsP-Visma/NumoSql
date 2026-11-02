using NumoSql.Execution;
using NumoSql.Parsing;
using NumoSql.Syntax;
using NumoSql.Transactions;

namespace NumoSql;

/// <summary>
/// A handle for running SQL against one database. Outside a transaction, each statement commits on its own.
/// BEGIN holds the database's write gate until COMMIT or ROLLBACK, so other sessions wait for it. A session is
/// not thread-safe: use one session per thread.
/// </summary>
public sealed class Session : IDisposable
{
    private readonly Database database;
    private Transaction? openTransaction;

    internal Session(Database database)
    {
        this.database = database;
    }

    /// <summary>
    /// Runs every statement in the text, in order. The whole text is parsed first, so a syntax error anywhere
    /// runs nothing. A runtime error stops the script; statements before it have already committed when they ran
    /// outside a transaction.
    /// </summary>
    public IReadOnlyList<QueryResult> Execute(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var results = new List<QueryResult>();
        foreach (var parsed in SqlParser.ParseScript(sql))
        {
            results.Add(ExecuteStatement(parsed));
        }

        return results;
    }

    /// <summary>Rolls back any open transaction and releases the write gate.</summary>
    public void Dispose()
    {
        if (openTransaction is not null)
        {
            RollBackOpenTransaction();
        }
    }

    private QueryResult ExecuteStatement(ParsedStatement parsed) => parsed.Statement switch
    {
        BeginStatement => BeginTransaction(),
        CommitStatement => CommitTransaction(),
        RollbackStatement => RollbackTransaction(),
        CheckpointStatement => Checkpoint(),
        _ => ExecuteDataStatement(parsed),
    };

    private QueryResult BeginTransaction()
    {
        if (openTransaction is not null)
        {
            throw new NumoSqlException("a transaction is already in progress");
        }

        database.AcquireWriteGate();
        openTransaction = new Transaction();
        return QueryResult.Command("BEGIN");
    }

    private QueryResult CommitTransaction()
    {
        var transaction = RequireOpenTransaction();
        try
        {
            // An aborted transaction was already rolled back when the error happened, so COMMIT only ends it.
            if (transaction.IsAborted)
            {
                return QueryResult.Command("ROLLBACK");
            }

            database.CommitTransaction(transaction);
            return QueryResult.Command("COMMIT");
        }
        finally
        {
            EndTransaction();
        }
    }

    private QueryResult RollbackTransaction()
    {
        RequireOpenTransaction();
        RollBackOpenTransaction();
        return QueryResult.Command("ROLLBACK");
    }

    private QueryResult Checkpoint()
    {
        if (openTransaction is not null)
        {
            throw new NumoSqlException("CHECKPOINT cannot run inside a transaction");
        }

        database.Checkpoint();
        return QueryResult.Command("CHECKPOINT");
    }

    private QueryResult ExecuteDataStatement(ParsedStatement parsed)
    {
        if (openTransaction is not null)
        {
            return ExecuteInTransaction(openTransaction, parsed);
        }

        return database.WithWriteGate(() => ExecuteAutocommit(parsed));
    }

    private QueryResult ExecuteAutocommit(ParsedStatement parsed)
    {
        var transaction = new Transaction();
        QueryResult result;
        try
        {
            result = RunStatement(transaction, parsed);
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }

        database.CommitTransaction(transaction);
        return result;
    }

    /// <summary>
    /// An error undoes the transaction's changes at once and aborts it, as in PostgreSQL. Every later statement is
    /// refused until ROLLBACK or COMMIT, so the caller cannot continue on top of a half-applied change.
    /// </summary>
    private QueryResult ExecuteInTransaction(Transaction transaction, ParsedStatement parsed)
    {
        if (transaction.IsAborted)
        {
            throw new NumoSqlException(
                "current transaction is aborted, commands ignored until end of transaction block");
        }

        try
        {
            return RunStatement(transaction, parsed);
        }
        catch (Exception)
        {
            transaction.Rollback();
            transaction.MarkAborted();
            throw;
        }
    }

    private QueryResult RunStatement(Transaction transaction, ParsedStatement parsed)
    {
        var context = new StatementContext(database.Tables, transaction);
        var result = StatementExecutor.Execute(parsed.Statement, context);
        if (!parsed.Statement.IsReadOnly)
        {
            transaction.RecordStatement(parsed.SourceText);
        }

        return result;
    }

    private Transaction RequireOpenTransaction() =>
        openTransaction ?? throw new NumoSqlException("no transaction is in progress");

    private void RollBackOpenTransaction()
    {
        try
        {
            openTransaction?.Rollback();
        }
        finally
        {
            EndTransaction();
        }
    }

    private void EndTransaction()
    {
        openTransaction = null;
        database.ReleaseWriteGate();
    }
}
