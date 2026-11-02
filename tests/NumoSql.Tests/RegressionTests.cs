using System.Buffers.Binary;
using NumoSql.Parsing;
using NumoSql.Storage;
using Xunit;

namespace NumoSql.Tests;

public sealed class RegressionTests
{
    [Fact]
    public void Line_comment_ends_at_a_carriage_return()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1);");

        session.Execute("DELETE FROM t WHERE id = 1; -- purge\rINSERT INTO t VALUES (2);");

        Assert.Equal(new object?[] { 2L }, session.FirstColumn("SELECT id FROM t"));
    }

    [Fact]
    public void Numeric_literal_followed_by_a_letter_is_a_syntax_error()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript("SELECT 12abc"));
    }

    [Fact]
    public void Leading_and_trailing_decimal_point_forms_are_real_literals()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.Equal(new object?[] { 0.5, 5.0 }, session.Rows("SELECT .5, 5.")[0]);
    }

    [Fact]
    public void Unary_plus_is_accepted_on_numbers_and_rejected_on_text()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.Equal(5L, session.Scalar("SELECT +5"));
        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT +'a'"));
    }

    [Fact]
    public void Lone_surrogate_in_text_is_rejected_and_nothing_is_written()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (s TEXT);");

        Assert.ThrowsAny<SqlSyntaxException>(() => session.Execute("INSERT INTO t VALUES ('a" + '\uD800' + "')"));

        Assert.Equal(0L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Surrogate_pair_text_survives_reopen()
    {
        using var directory = new TemporaryDirectory();
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute("CREATE TABLE t (s TEXT); INSERT INTO t VALUES ('a\U0001F600b');");
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal("a\U0001F600b", reopened.CreateSession().Scalar("SELECT s FROM t"));
    }

    [Fact]
    public void Unquoted_identifier_may_contain_a_combining_mark()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        session.Execute("CREATE TABLE café (a INTEGER); INSERT INTO café VALUES (1);");

        Assert.Equal(1L, session.Scalar("SELECT COUNT(*) FROM café"));
    }

    [Fact]
    public void Table_level_primary_key_is_enforced()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (a INTEGER, b TEXT, PRIMARY KEY (a)); INSERT INTO t VALUES (1, 'x');");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES (1, 'y')"));
    }

    [Fact]
    public void Table_level_primary_key_naming_an_unknown_column_is_rejected()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript("CREATE TABLE t (a INTEGER, PRIMARY KEY (zzz))"));
    }

    [Fact]
    public void Stray_comma_after_limit_is_not_reported_as_a_table_list()
    {
        var error = Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(
            "SELECT a FROM t ORDER BY a LIMIT 5, 10"));

        Assert.DoesNotContain("table lists", error.Message);
    }

    [Fact]
    public void Limit_does_not_evaluate_rows_outside_the_page()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (x INTEGER); INSERT INTO t VALUES (1), (0);");

        Assert.Equal(new object?[] { 10L }, session.FirstColumn("SELECT 10 / x FROM t LIMIT 1"));
    }

    [Fact]
    public void Average_that_overflows_is_an_error_rather_than_infinity()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE r (v REAL); INSERT INTO r VALUES (1.7e308), (1.7e308), (-1.7e308);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT AVG(v) FROM r"));
    }

    [Fact]
    public void Order_by_can_name_an_output_column_that_is_not_aliased()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute(
            "CREATE TABLE a (id INTEGER); CREATE TABLE b (id INTEGER, y INTEGER);" +
            "INSERT INTO a VALUES (2), (1); INSERT INTO b VALUES (1, 10), (2, 20);");

        Assert.Equal(
            new object?[] { 1L, 2L },
            session.FirstColumn("SELECT a.id FROM a JOIN b ON a.id = b.id ORDER BY id"));
    }

    [Fact]
    public void Order_by_name_shared_by_two_output_columns_is_ambiguous()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT id, id FROM t ORDER BY id"));
    }

    [Fact]
    public void Rolling_back_an_update_and_a_delete_restores_the_rows()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY, v INTEGER); INSERT INTO t VALUES (1, 10), (2, 20);");

        session.Execute("BEGIN; UPDATE t SET v = v + 1; DELETE FROM t WHERE id = 1; ROLLBACK;");

        Assert.Equal(new object?[] { 10L, 20L }, session.FirstColumn("SELECT v FROM t ORDER BY id"));
    }

    [Fact]
    public void Rolling_back_a_drop_table_restores_the_table_and_its_rows()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (7);");

        session.Execute("BEGIN; DROP TABLE t; ROLLBACK;");

        Assert.Equal(new object?[] { 7L }, session.FirstColumn("SELECT id FROM t"));
    }

    [Fact]
    public void Incomplete_header_at_the_end_of_the_log_is_a_torn_write_not_corruption()
    {
        using var directory = new TemporaryDirectory();
        var walPath = new DatabaseFiles(directory.DirectoryPath).WalPath;
        using (var database = Database.Open(directory.DirectoryPath))
        using (var session = database.CreateSession())
        {
            session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1);");
        }

        var committedLength = new FileInfo(walPath).Length;
        var torn = new byte[WalFrame.HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(torn, 40);
        using (var stream = new FileStream(walPath, FileMode.Append, FileAccess.Write))
        {
            stream.Write(torn);
        }

        using var reopened = Database.Open(directory.DirectoryPath);
        Assert.Equal(new object?[] { 1L }, reopened.CreateSession().FirstColumn("SELECT id FROM t"));
        Assert.Equal(committedLength, new FileInfo(walPath).Length);
    }

    [Fact]
    public void Null_script_is_reported_as_an_argument_error()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.ThrowsAny<ArgumentNullException>(() => session.Execute(null!));
    }
}
