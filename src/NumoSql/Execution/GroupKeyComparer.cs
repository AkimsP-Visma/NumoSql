namespace NumoSql.Execution;

/// <summary>Compares GROUP BY key arrays element by element. Two NULLs form one group, as in SQL.</summary>
internal sealed class GroupKeyComparer : IEqualityComparer<object?[]>
{
    public static GroupKeyComparer Instance { get; } = new();

    public bool Equals(object?[]? left, object?[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (!object.Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(object?[] keys)
    {
        var hash = new HashCode();
        foreach (var key in keys)
        {
            hash.Add(key);
        }

        return hash.ToHashCode();
    }
}
