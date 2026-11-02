using Xunit;

namespace NumoSql.Tests;

public sealed class TransactionTests
{
    [Fact]
    public void Rollback_discards_changes()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER);");

        session.Execute("BEGIN; INSERT INTO t VALUES (1); ROLLBACK;");

        Assert.Equal(0L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Commit_makes_changes_visible_to_other_sessions()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); BEGIN; INSERT INTO t VALUES (1); COMMIT;");

        using var other = database.CreateSession();
        Assert.Equal(1L, other.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Error_aborts_the_transaction_until_rollback()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1);");

        session.Execute("BEGIN; INSERT INTO t VALUES (2);");
        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES (1)"));

        var refused = Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT COUNT(*) FROM t"));
        Assert.Contains("aborted", refused.Message);

        var commitResult = session.Execute("COMMIT")[^1];
        Assert.Equal("ROLLBACK", commitResult.CommandTag);
        Assert.Equal(1L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public async Task Second_session_waits_until_the_open_transaction_ends()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); BEGIN; INSERT INTO t VALUES (1);");

        using var other = database.CreateSession();
        var waiting = Task.Run(() => other.Execute("INSERT INTO t VALUES (2)"));
        var firstToFinish = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMilliseconds(300)));
        Assert.NotSame(waiting, firstToFinish);

        session.Execute("ROLLBACK");

        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1L, session.Scalar("SELECT COUNT(*) FROM t"));
        Assert.Equal(2L, session.Scalar("SELECT id FROM t"));
    }

    [Fact]
    public void Checkpoint_inside_a_transaction_is_rejected_without_aborting_it()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); BEGIN;");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("CHECKPOINT"));

        session.Execute("INSERT INTO t VALUES (1); COMMIT;");
        Assert.Equal(1L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Failed_autocommit_statement_changes_nothing()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES (1), (1)"));

        Assert.Equal(0L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Rollback_without_a_transaction_is_an_error()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("ROLLBACK"));
    }

    [Fact]
    public void Create_table_is_rolled_back_with_the_transaction()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        session.Execute("BEGIN; CREATE TABLE gone (x INTEGER); ROLLBACK;");

        Assert.Empty(session.Rows("SHOW TABLES"));
    }

    [Fact]
    public void Disposing_a_session_with_an_open_transaction_rolls_it_back()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using (var session = database.CreateSession())
        {
            session.Execute("CREATE TABLE t (id INTEGER); BEGIN; INSERT INTO t VALUES (1);");
        }

        using var next = database.CreateSession();
        Assert.Equal(0L, next.Scalar("SELECT COUNT(*) FROM t"));
    }
}
