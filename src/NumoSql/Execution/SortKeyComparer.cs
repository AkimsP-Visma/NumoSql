using NumoSql.Types;

namespace NumoSql.Execution;

/// <summary>Orders rows by a list of sort keys. Stable when used with LINQ OrderBy, so equal rows keep their order.</summary>
internal sealed class SortKeyComparer : IComparer<object?[]>
{
    private readonly IReadOnlyList<bool> isDescending;

    public SortKeyComparer(IReadOnlyList<bool> isDescending)
    {
        this.isDescending = isDescending;
    }

    public int Compare(object?[]? left, object?[]? right)
    {
        for (var index = 0; index < isDescending.Count; index++)
        {
            var comparison = CompareNullsLast(left![index], right![index]);
            if (comparison != 0)
            {
                return isDescending[index] ? -comparison : comparison;
            }
        }

        return 0;
    }

    /// <summary>NULL sorts after every other value, so it comes first under DESC. This matches PostgreSQL.</summary>
    private static int CompareNullsLast(object? left, object? right)
    {
        if (left is null)
        {
            return right is null ? 0 : 1;
        }

        if (right is null)
        {
            return -1;
        }

        return ValueOrdering.Compare(left, right);
    }
}
