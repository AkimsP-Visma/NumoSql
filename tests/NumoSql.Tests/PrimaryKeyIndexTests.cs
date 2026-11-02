using Xunit;

namespace NumoSql.Tests;

public sealed class PrimaryKeyIndexTests
{
    [Fact]
    public void Equality_on_the_primary_key_returns_the_matching_row()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO t VALUES (1, 'a'), (2, 'b'), (3, 'c');");

        Assert.Equal("b", session.Scalar("SELECT name FROM t WHERE id = 2"));
        Assert.Equal("b", session.Scalar("SELECT name FROM t WHERE 2 = id"));
        Assert.Equal("b", session.Scalar("SELECT t.name FROM t WHERE t.id = 2"));
    }

    [Fact]
    public void Equality_on_a_missing_key_returns_no_rows()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1);");

        Assert.Empty(session.Rows("SELECT id FROM t WHERE id = 99"));
    }

    [Fact]
    public void Integer_literal_finds_a_real_primary_key_stored_as_real()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE r (k REAL PRIMARY KEY, v TEXT); INSERT INTO r VALUES (1, 'one');");

        Assert.Equal("one", session.Scalar("SELECT v FROM r WHERE k = 1"));
    }

    [Fact]
    public void Text_primary_key_is_found_by_equality()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE kv (k TEXT PRIMARY KEY, v INTEGER); INSERT INTO kv VALUES ('alpha', 7);");

        Assert.Equal(7L, session.Scalar("SELECT v FROM kv WHERE k = 'alpha'"));
    }

    [Fact]
    public void Index_follows_an_update_of_the_key()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER); INSERT INTO t VALUES (1, 10);");

        session.Execute("UPDATE t SET id = 5 WHERE id = 1;");

        Assert.Empty(session.Rows("SELECT v FROM t WHERE id = 1"));
        Assert.Equal(10L, session.Scalar("SELECT v FROM t WHERE id = 5"));
    }

    [Fact]
    public void Index_forgets_a_row_whose_insert_was_rolled_back()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1);");

        session.Execute("BEGIN; INSERT INTO t VALUES (9); ROLLBACK;");

        Assert.Empty(session.Rows("SELECT id FROM t WHERE id = 9"));
        Assert.Equal(1L, session.Scalar("SELECT id FROM t WHERE id = 1"));
    }

    [Fact]
    public void Text_literal_against_an_integer_key_keeps_its_type_error()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT id FROM t WHERE id = 'x'"));
    }
}
