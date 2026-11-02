namespace NumoSql.Syntax;

internal abstract record Expression;

/// <summary>A constant. Value is long, double, string, bool or null.</summary>
internal sealed record LiteralExpression(object? Value) : Expression;

/// <summary>A column, optionally qualified by a table name or alias.</summary>
internal sealed record ColumnReference(string? Qualifier, string Name) : Expression;

/// <summary>The bare * in a select list. Only valid there, so the executor expands it.</summary>
internal sealed record WildcardExpression : Expression;

internal sealed record UnaryExpression(UnaryOperator Operator, Expression Operand) : Expression;

internal sealed record BinaryExpression(BinaryOperator Operator, Expression Left, Expression Right) : Expression;

internal sealed record IsNullExpression(Expression Operand, bool IsNegated) : Expression;

/// <summary>A function call. COUNT(*) is the only call that takes the wildcard argument.</summary>
internal sealed record FunctionCall(string Name, IReadOnlyList<Expression> Arguments, bool IsWildcardArgument)
    : Expression;

internal enum UnaryOperator
{
    Not,
    Negate,
    Plus,
}

internal enum BinaryOperator
{
    Or,
    And,
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
    Concatenate,
    Add,
    Subtract,
    Multiply,
    Divide,
}
