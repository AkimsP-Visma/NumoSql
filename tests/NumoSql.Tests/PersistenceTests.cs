using System.Buffers.Binary;
using NumoSql.Storage;
using Xunit;

namespace NumoSql.Tests;

public sealed class PersistenceTests
{
    private const string TableScript = "CREATE TABLE t (id INTEGER PRIMARY KEY, v TEXT);";

    [Fact]
    public void Committed_data_survives_reopen()
    {
        using var directory = new TemporaryDirectory();
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'one'), (2, 'two');");
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        using var reader = reopened.CreateSession();
        Assert.Equal("two", reader.Scalar("SELECT v FROM t WHERE id = 2"));
        Assert.Equal(2L, reader.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Open_transaction_is_not_durable_when_the_database_closes()
    {
        using var directory = new TemporaryDirectory();
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "BEGIN; INSERT INTO t VALUES (1, 'lost');");
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal(0L, reopened.CreateSession().Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Checkpoint_empties_the_log_and_data_survives()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'one');");
            Assert.True(new FileInfo(walPath).Length > 0);
            session.Execute("CHECKPOINT;");
        }

        Assert.Equal(0L, new FileInfo(walPath).Length);

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal("one", reopened.CreateSession().Scalar("SELECT v FROM t"));
    }

    [Fact]
    public void Torn_bytes_after_the_last_commit_are_discarded_on_recovery()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'kept');");
        }

        var committedLength = new FileInfo(walPath).Length;
        using (var stream = new FileStream(walPath, FileMode.Append, FileAccess.Write))
        {
            stream.Write(new byte[] { 0x17, 0x2A, 0x03, 0x91, 0x44 });
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal("kept", reopened.CreateSession().Scalar("SELECT v FROM t"));
        Assert.Equal(committedLength, new FileInfo(walPath).Length);
    }

    [Fact]
    public void Complete_header_without_its_payload_is_discarded_as_a_torn_write()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'kept');");
        }

        var bytes = File.ReadAllBytes(walPath);
        var committedLength = bytes.Length;
        var torn = bytes.Concat(bytes.Take(WalFrame.HeaderLength)).ToArray();
        File.WriteAllBytes(walPath, torn);

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal("kept", reopened.CreateSession().Scalar("SELECT v FROM t"));
        Assert.Equal(committedLength, new FileInfo(walPath).Length);
    }

    [Fact]
    public void Damaged_payload_before_later_commits_is_reported_not_skipped()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'a'); INSERT INTO t VALUES (2, 'b');");
        }

        var bytes = File.ReadAllBytes(walPath);
        var secondFrameStart = FrameStart(bytes, 1);
        bytes[secondFrameStart + WalFrame.HeaderLength + 5] ^= 0xFF;
        File.WriteAllBytes(walPath, bytes);

        Assert.ThrowsAny<DatabaseCorruptException>(() => Database.Open(directory.DirectoryPath));
    }

    [Fact]
    public void Damaged_record_header_before_later_commits_is_reported()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'a'); INSERT INTO t VALUES (2, 'b');");
        }

        var bytes = File.ReadAllBytes(walPath);
        bytes[FrameStart(bytes, 1)] ^= 0x01;
        File.WriteAllBytes(walPath, bytes);

        Assert.ThrowsAny<DatabaseCorruptException>(() => Database.Open(directory.DirectoryPath));
    }

    [Fact]
    public void Log_left_behind_by_an_interrupted_checkpoint_is_not_applied_twice()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'a'); INSERT INTO t VALUES (2, 'b');");
        }

        // Keeping a copy of the log and restoring it after the checkpoint simulates a crash between the
        // snapshot rename and the log truncation.
        var staleLog = File.ReadAllBytes(walPath);
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute("CHECKPOINT;");
        }

        File.WriteAllBytes(walPath, staleLog);

        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            Assert.Equal(2L, session.Scalar("SELECT COUNT(*) FROM t"));
            session.Execute("INSERT INTO t VALUES (3, 'c');");
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal(3L, reopened.CreateSession().Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Incomplete_checkpoint_file_is_removed_on_open()
    {
        using var directory = new TemporaryDirectory();
        var temporaryPath = new DatabaseFiles(directory.DirectoryPath).CheckpointPath + ".tmp";
        File.WriteAllBytes(temporaryPath, new byte[] { 1, 2, 3 });

        using var database = Database.Open(directory.DirectoryPath);

        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public void Damaged_checkpoint_is_reported_instead_of_loaded()
    {
        using var directory = new TemporaryDirectory();
        var checkpointPath = new DatabaseFiles(directory.DirectoryPath).CheckpointPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute(TableScript + "INSERT INTO t VALUES (1, 'a'); CHECKPOINT;");
        }

        var bytes = File.ReadAllBytes(checkpointPath);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(checkpointPath, bytes);

        Assert.ThrowsAny<DatabaseCorruptException>(() => Database.Open(directory.DirectoryPath));
    }

    [Fact]
    public void Second_open_of_the_same_database_is_refused_while_the_first_is_open()
    {
        using var directory = new TemporaryDirectory();
        using var first = Database.Open(directory.DirectoryPath);

        Assert.ThrowsAny<NumoSqlException>(() => Database.Open(directory.DirectoryPath));
    }

    [Fact]
    public void Crc32_matches_the_standard_check_value()
    {
        Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"u8));
    }

    /// <summary>Walks the length prefixes from the start of the log to find where a record begins.</summary>
    private static int FrameStart(byte[] log, int recordIndex)
    {
        var offset = 0;
        for (var index = 0; index < recordIndex; index++)
        {
            offset += WalFrame.HeaderLength + BinaryPrimitives.ReadInt32LittleEndian(log.AsSpan(offset));
        }

        return offset;
    }
}
