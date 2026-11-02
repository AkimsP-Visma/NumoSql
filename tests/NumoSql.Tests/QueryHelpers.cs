namespace NumoSql.Tests;

internal static class QueryHelpers
{
    public static IReadOnlyList<object?[]> Rows(this Session session, string sql) => session.Execute(sql)[^1].Rows;

    public static object? Scalar(this Session session, string sql) => session.Rows(sql)[0][0];

    public static object?[] FirstColumn(this Session session, string sql) =>
        session.Rows(sql).Select(row => row[0]).ToArray();
}
