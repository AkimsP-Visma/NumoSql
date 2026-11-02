using NumoSql.Execution;
using NumoSql.Parsing;
using NumoSql.Schema;
using NumoSql.Storage;
using NumoSql.Transactions;

namespace NumoSql;

/// <summary>
/// A NUMOSQL database stored in a directory. A transaction reaches the write-ahead log before it is final, so a
/// crash loses only transactions that were never reported as committed. Checkpoints bound the log's size.
/// </summary>
public sealed class Database : IDisposable
{
    private readonly DatabaseFiles files;
    private readonly Catalog catalog;
    private readonly WriteAheadLog wal;
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private long lastLogSequenceNumber;
    private bool isDisposed;

    private Database(DatabaseFiles files, Catalog catalog, WriteAheadLog wal, long checkpointLogSequenceNumber)
    {
        this.files = files;
        this.catalog = catalog;
        this.wal = wal;
        lastLogSequenceNumber = checkpointLogSequenceNumber;
    }

    /// <summary>
    /// Opens the database in the directory, creating it if needed. Recovery loads the latest checkpoint, then
    /// replays the log. The log is opened first because its lock keeps a second process from recovering the same
    /// files at the same time.
    /// </summary>
    public static Database Open(string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new DatabaseFiles(directory);
        var wal = OpenWriteAheadLog(files.WalPath);

        try
        {
            CheckpointFile.DeleteIncomplete(files.CheckpointPath);
            var catalog = new Catalog();
            var checkpointLogSequenceNumber = CheckpointFile.Load(files.CheckpointPath, catalog);

            var database = new Database(files, catalog, wal, checkpointLogSequenceNumber);
            wal.Replay(database.ReplayRecord);
            return database;
        }
        catch (Exception)
        {
            wal.Dispose();
            throw;
        }
    }

    public Session CreateSession()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return new Session(this);
    }

    /// <summary>Close every session before disposing the database. Sessions that hold a transaction block the gate.</summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        wal.Dispose();
        writeGate.Dispose();
    }

    internal Catalog Tables => catalog;

    /// <summary>
    /// Makes the transaction final. Its statements are logged first. If the log write fails, the transaction is
    /// rolled back and the failure reaches the caller.
    /// </summary>
    internal void CommitTransaction(Transaction transaction)
    {
        if (transaction.LoggedStatements.Count == 0)
        {
            transaction.Complete();
            return;
        }

        var record = new WalRecord(lastLogSequenceNumber + 1, transaction.LoggedStatements);
        try
        {
            wal.Append(record);
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }

        lastLogSequenceNumber = record.LogSequenceNumber;
        transaction.Complete();
    }

    internal void AcquireWriteGate() => writeGate.Wait();

    internal void ReleaseWriteGate() => writeGate.Release();

    internal T WithWriteGate<T>(Func<T> action)
    {
        writeGate.Wait();
        try
        {
            return action();
        }
        finally
        {
            writeGate.Release();
        }
    }

    /// <summary>Writes a snapshot of the committed state, then empties the log. The snapshot is durable before the cut.</summary>
    internal void Checkpoint()
    {
        writeGate.Wait();
        try
        {
            CheckpointFile.Write(files.CheckpointPath, catalog, lastLogSequenceNumber);
            wal.Truncate();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new NumoSqlException($"checkpoint failed: {failure.Message}", failure);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static WriteAheadLog OpenWriteAheadLog(string path)
    {
        try
        {
            return new WriteAheadLog(path);
        }
        catch (IOException openFailure)
        {
            throw new NumoSqlException(
                $"cannot open the database log at {path}; it may be open in another process",
                openFailure);
        }
    }

    private void ReplayRecord(WalRecord record)
    {
        // A checkpoint can be written before the log is emptied. If the process stops in between, the log still
        // holds records the snapshot already contains, so they are skipped rather than applied twice.
        if (record.LogSequenceNumber <= lastLogSequenceNumber)
        {
            return;
        }

        if (record.LogSequenceNumber != lastLogSequenceNumber + 1)
        {
            throw new DatabaseCorruptException(
                $"the log jumps from record {lastLogSequenceNumber} to record {record.LogSequenceNumber}");
        }

        var transaction = new Transaction();
        var context = new StatementContext(catalog, transaction);
        try
        {
            foreach (var statementText in record.Statements)
            {
                ReplayStatement(context, statementText, record.LogSequenceNumber);
            }
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }

        transaction.Complete();
        lastLogSequenceNumber = record.LogSequenceNumber;
    }

    /// <summary>
    /// Re-runs a logged statement. Replay is deterministic because the dialect has no clock, random or session
    /// state, so a statement that succeeded once succeeds again against the same state.
    /// </summary>
    private static void ReplayStatement(StatementContext context, string statementText, long logSequenceNumber)
    {
        try
        {
            var statement = SqlParser.ParseScript(statementText).Single().Statement;
            StatementExecutor.Execute(statement, context);
        }
        catch (NumoSqlException failure)
        {
            throw new DatabaseCorruptException(
                $"log record {logSequenceNumber} cannot be replayed: {failure.Message}",
                failure);
        }
    }
}
