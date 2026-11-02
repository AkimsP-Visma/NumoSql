namespace NumoSql.Syntax;

internal static class ExpressionTree
{
    /// <summary>
    /// Returns the depth of the tree, or a value above <paramref name="limit"/> as soon as that limit is
    /// exceeded. Iterative, so a deep tree cannot exhaust the call stack while it is measured.
    /// </summary>
    public static int DepthUpTo(Expression root, int limit)
    {
        var pending = new Stack<(Expression Node, int Depth)>();
        pending.Push((root, 1));
        var deepest = 0;

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            deepest = Math.Max(deepest, depth);
            if (deepest > limit)
            {
                return deepest;
            }

            foreach (var child in ChildrenOf(node))
            {
                pending.Push((child, depth + 1));
            }
        }

        return deepest;
    }

    public static bool Any(Expression root, Func<Expression, bool> predicate)
    {
        var pending = new Stack<Expression>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (predicate(node))
            {
                return true;
            }

            foreach (var child in ChildrenOf(node))
            {
                pending.Push(child);
            }
        }

        return false;
    }

    private static IEnumerable<Expression> ChildrenOf(Expression node) => node switch
    {
        UnaryExpression unary => [unary.Operand],
        BinaryExpression binary => [binary.Left, binary.Right],
        IsNullExpression isNull => [isNull.Operand],
        FunctionCall call => call.Arguments,
        _ => [],
    };
}
