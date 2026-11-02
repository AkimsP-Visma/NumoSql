using NumoSql.Parsing;
using Xunit;

namespace NumoSql.Tests;

public sealed class ParserTests
{
    [Fact]
    public void Script_splits_statements_and_keeps_each_source_text()
    {
        var statements = SqlParser.ParseScript("SELECT 1; SELECT 2;");

        Assert.Equal(2, statements.Count);
        Assert.Equal("SELECT 1", statements[0].SourceText);
        Assert.Equal("SELECT 2", statements[1].SourceText);
    }

    [Fact]
    public void Left_join_is_rejected_instead_of_becoming_an_inner_join()
    {
        // Read as an alias plus an inner join, this would silently return different rows.
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(
            "SELECT * FROM a LEFT JOIN b ON a.x = b.x"));
    }

    [Fact]
    public void Order_by_position_number_is_rejected()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript("SELECT a FROM t ORDER BY 1"));
    }

    [Fact]
    public void Unsupported_clauses_are_named_in_the_error()
    {
        var error = Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(
            "SELECT region FROM sales GROUP BY region HAVING COUNT(*) > 1"));

        Assert.Contains("HAVING is not supported", error.Message);
    }

    [Fact]
    public void Table_with_two_primary_keys_is_rejected()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(
            "CREATE TABLE t (a INTEGER PRIMARY KEY, b INTEGER PRIMARY KEY)"));
    }

    [Fact]
    public void Deeply_nested_parentheses_are_rejected_before_they_can_exhaust_the_stack()
    {
        var script = "SELECT " + new string('(', 5000) + "1" + new string(')', 5000);

        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(script));
    }

    [Fact]
    public void Very_long_operator_chain_is_rejected_before_evaluation_can_exhaust_the_stack()
    {
        var script = "SELECT " + string.Join(" + ", Enumerable.Repeat("1", 600));

        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript(script));
    }

    [Fact]
    public void Integer_literal_beyond_long_range_is_rejected()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => SqlParser.ParseScript("SELECT 9223372036854775808"));
    }
}
