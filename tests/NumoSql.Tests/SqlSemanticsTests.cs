using Xunit;

namespace NumoSql.Tests;

public sealed class SqlSemanticsTests
{
    [Fact]
    public void Arithmetic_follows_precedence_and_integer_division_truncates()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        var row = session.Rows("SELECT 1 + 2 * 3, 7 / 2, -7 / 2, 7.0 / 2")[0];

        Assert.Equal(new object?[] { 7L, 3L, -3L, 3.5 }, row);
    }

    [Fact]
    public void Minimum_integer_literal_parses()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.Equal(long.MinValue, session.Scalar("SELECT -9223372036854775808"));
    }

    [Fact]
    public void Division_by_zero_is_an_error()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        var error = Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT 1 / 0"));

        Assert.Equal("division by zero", error.Message);
    }

    [Fact]
    public void Integer_overflow_is_an_error_not_a_wrap_around()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT 9223372036854775807 + 1"));
    }

    [Fact]
    public void Three_valued_logic_yields_null_where_sql_says_unknown()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        var row = session.Rows("SELECT NULL = NULL, NULL AND FALSE, NULL OR TRUE, NOT NULL")[0];

        Assert.Equal(new object?[] { null, false, true, null }, row);
    }

    [Fact]
    public void Where_keeps_only_rows_whose_condition_is_true()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER, v INTEGER); INSERT INTO t VALUES (1, 5), (2, NULL), (3, -1);");

        Assert.Equal(new object?[] { 1L }, session.FirstColumn("SELECT id FROM t WHERE v > 0"));
        Assert.Equal(new object?[] { 3L }, session.FirstColumn("SELECT id FROM t WHERE NOT (v > 0)"));
        Assert.Equal(new object?[] { 2L }, session.FirstColumn("SELECT id FROM t WHERE v IS NULL"));
    }

    [Fact]
    public void Concatenation_requires_text_operands()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.Equal("ab", session.Scalar("SELECT 'a' || 'b'"));
        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT 'a' || 1"));
    }

    [Fact]
    public void Order_by_puts_nulls_last_ascending_and_first_descending()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (v INTEGER); INSERT INTO t VALUES (2), (NULL), (1);");

        Assert.Equal(new object?[] { 1L, 2L, null }, session.FirstColumn("SELECT v FROM t ORDER BY v"));
        Assert.Equal(new object?[] { null, 2L, 1L }, session.FirstColumn("SELECT v FROM t ORDER BY v DESC"));
    }

    [Fact]
    public void Limit_and_offset_page_the_sorted_rows()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1), (2), (3), (4), (5);");

        Assert.Equal(new object?[] { 2L, 3L }, session.FirstColumn("SELECT id FROM t ORDER BY id LIMIT 2 OFFSET 1"));
    }

    [Fact]
    public void Order_by_can_name_an_output_alias()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1), (3), (2);");

        Assert.Equal(
            new object?[] { 6L, 4L, 2L },
            session.FirstColumn("SELECT id * 2 AS doubled FROM t ORDER BY doubled DESC"));
    }

    [Fact]
    public void Inner_join_keeps_only_matching_pairs()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute(
            "CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT);" +
            "CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER, total INTEGER);" +
            "INSERT INTO customers VALUES (1, 'Ana'), (2, 'Bo'), (3, 'Cy');" +
            "INSERT INTO orders VALUES (10, 1, 50), (11, 1, 20), (12, 3, 70);");

        var rows = session.Rows(
            "SELECT c.name, o.total FROM customers AS c INNER JOIN orders AS o ON o.customer_id = c.id ORDER BY o.total");

        Assert.Equal(new object?[] { "Ana", 20L }, rows[0]);
        Assert.Equal(new object?[] { "Ana", 50L }, rows[1]);
        Assert.Equal(new object?[] { "Cy", 70L }, rows[2]);
        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void Unqualified_column_present_in_two_tables_is_ambiguous()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE a (id INTEGER); CREATE TABLE b (id INTEGER);");

        var error = Assert.ThrowsAny<NumoSqlException>(() => session.Execute(
            "SELECT id FROM a JOIN b ON a.id = b.id"));

        Assert.Contains("ambiguous", error.Message);
    }

    [Fact]
    public void Same_table_twice_without_aliases_is_rejected()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE a (id INTEGER);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT * FROM a JOIN a ON a.id = a.id"));
    }

    [Fact]
    public void Group_by_computes_each_aggregate_per_group()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute(
            "CREATE TABLE sales (region TEXT, amount INTEGER);" +
            "INSERT INTO sales VALUES ('east', 10), ('west', 5), ('east', 20), ('west', NULL);");

        var rows = session.Rows(
            "SELECT region, COUNT(*), COUNT(amount), SUM(amount), MIN(amount), MAX(amount), AVG(amount) " +
            "FROM sales GROUP BY region ORDER BY region");

        Assert.Equal(new object?[] { "east", 2L, 2L, 30L, 10L, 20L, 15.0 }, rows[0]);
        Assert.Equal(new object?[] { "west", 2L, 1L, 5L, 5L, 5L, 5.0 }, rows[1]);
    }

    [Fact]
    public void Order_by_can_sort_groups_by_an_aggregate_alias()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute(
            "CREATE TABLE sales (region TEXT, amount INTEGER);" +
            "INSERT INTO sales VALUES ('east', 10), ('west', 5), ('east', 20);");

        Assert.Equal(
            new object?[] { "east", "west" },
            session.FirstColumn("SELECT region, SUM(amount) AS total FROM sales GROUP BY region ORDER BY total DESC"));
    }

    [Fact]
    public void Aggregate_over_empty_table_counts_zero_and_sums_to_null()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE empty_table (x INTEGER);");

        var row = session.Rows("SELECT COUNT(*), SUM(x) FROM empty_table")[0];

        Assert.Equal(new object?[] { 0L, null }, row);
    }

    [Fact]
    public void Bare_column_outside_group_by_is_rejected()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE sales (region TEXT, amount INTEGER);");

        var error = Assert.ThrowsAny<NumoSqlException>(() => session.Execute(
            "SELECT region, amount, COUNT(*) FROM sales GROUP BY region"));

        Assert.Contains("must appear in the GROUP BY clause", error.Message);
    }

    [Fact]
    public void Aggregate_in_where_is_rejected()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE sales (amount INTEGER);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT amount FROM sales WHERE COUNT(*) > 1"));
    }

    [Fact]
    public void Primary_key_rejects_duplicates_and_leaves_the_table_unchanged()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1), (2);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES (3), (1)"));

        Assert.Equal(new object?[] { 1L, 2L }, session.FirstColumn("SELECT id FROM t ORDER BY id"));
    }

    [Fact]
    public void Update_that_collides_on_primary_key_leaves_the_table_unchanged()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (1), (2);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("UPDATE t SET id = 1 WHERE id = 2"));

        Assert.Equal(new object?[] { 1L, 2L }, session.FirstColumn("SELECT id FROM t ORDER BY id"));
    }

    [Fact]
    public void Update_reads_original_values_so_assignments_can_swap()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE p (a INTEGER, b INTEGER); INSERT INTO p VALUES (1, 2); UPDATE p SET a = b, b = a;");

        Assert.Equal(new object?[] { 2L, 1L }, session.Rows("SELECT a, b FROM p")[0]);
    }

    [Fact]
    public void Delete_reports_the_number_of_rows_removed()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER); INSERT INTO t VALUES (1), (2), (3);");

        var result = session.Execute("DELETE FROM t WHERE id > 1")[^1];

        Assert.Equal(2, result.RowsAffected);
        Assert.Equal(1L, session.Scalar("SELECT COUNT(*) FROM t"));
    }

    [Fact]
    public void Not_null_column_rejects_null()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (a INTEGER NOT NULL);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES (NULL)"));
    }

    [Fact]
    public void Text_value_is_rejected_for_integer_column()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (a INTEGER);");

        Assert.ThrowsAny<NumoSqlException>(() => session.Execute("INSERT INTO t VALUES ('x')"));
    }

    [Fact]
    public void Integer_is_widened_into_real_column()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE m (v REAL); INSERT INTO m VALUES (3);");

        Assert.Equal(3.0, Assert.IsType<double>(session.Scalar("SELECT v FROM m")));
    }

    [Fact]
    public void Key_and_value_work_as_column_names()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE kv (key TEXT PRIMARY KEY, value TEXT); INSERT INTO kv VALUES ('a', 'x');");

        Assert.Equal(new object?[] { "a", "x" }, session.Rows("SELECT key, value FROM kv")[0]);
    }

    [Fact]
    public void Show_tables_lists_names_in_order_and_drop_if_exists_is_quiet()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE b (x INTEGER); CREATE TABLE a (x INTEGER);");

        Assert.Equal(new object?[] { "a", "b" }, session.FirstColumn("SHOW TABLES"));

        session.Execute("DROP TABLE IF EXISTS missing;");
        Assert.Equal(new object?[] { "a", "b" }, session.FirstColumn("SHOW TABLES"));
    }

    [Fact]
    public void Select_without_from_yields_one_row()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();

        Assert.Equal(2L, session.Scalar("SELECT 1 + 1 AS two"));
    }

    [Fact]
    public void Unknown_column_is_named_in_the_error()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (id INTEGER);");

        var error = Assert.ThrowsAny<NumoSqlException>(() => session.Execute("SELECT nope FROM t"));

        Assert.Contains("nope", error.Message);
    }

    [Fact]
    public void Like_is_not_silently_ignored()
    {
        using var directory = new TemporaryDirectory();
        using var database = Database.Open(directory.DirectoryPath);
        using var session = database.CreateSession();
        session.Execute("CREATE TABLE t (name TEXT);");

        Assert.ThrowsAny<SqlSyntaxException>(() => session.Execute("SELECT name FROM t WHERE name LIKE 'a%'"));
    }
}
