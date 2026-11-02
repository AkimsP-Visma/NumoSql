using System.Globalization;
using NumoSql.Lexing;
using NumoSql.Syntax;

namespace NumoSql.Parsing;

/// <summary>
/// Precedence climbing, loosest to tightest: OR, AND, NOT, IS [NOT] NULL, comparisons, ||, + -, * /, unary -.
/// </summary>
internal sealed class ExpressionParser
{
    /// <summary>Bounds recursive descent through NOT, unary minus and parentheses.</summary>
    private const int MaximumNestingDepth = 200;

    /// <summary>Bounds the tree itself, because left-associative chains build trees that are deep but not recursive.</summary>
    private const int MaximumExpressionDepth = 500;

    private const string NegationSymbol = "-";
    private const string PlusSymbol = "+";

    private static readonly IReadOnlyDictionary<string, BinaryOperator> ComparisonOperators =
        new Dictionary<string, BinaryOperator>(StringComparer.Ordinal)
        {
            ["="] = BinaryOperator.Equal,
            ["<>"] = BinaryOperator.NotEqual,
            ["!="] = BinaryOperator.NotEqual,
            ["<"] = BinaryOperator.Less,
            ["<="] = BinaryOperator.LessOrEqual,
            [">"] = BinaryOperator.Greater,
            [">="] = BinaryOperator.GreaterOrEqual,
        };

    private static readonly IReadOnlyDictionary<string, BinaryOperator> ConcatenationOperators =
        new Dictionary<string, BinaryOperator>(StringComparer.Ordinal) { ["||"] = BinaryOperator.Concatenate };

    private static readonly IReadOnlyDictionary<string, BinaryOperator> AdditiveOperators =
        new Dictionary<string, BinaryOperator>(StringComparer.Ordinal)
        {
            ["+"] = BinaryOperator.Add,
            ["-"] = BinaryOperator.Subtract,
        };

    private static readonly IReadOnlyDictionary<string, BinaryOperator> MultiplicativeOperators =
        new Dictionary<string, BinaryOperator>(StringComparer.Ordinal)
        {
            ["*"] = BinaryOperator.Multiply,
            ["/"] = BinaryOperator.Divide,
        };

    private readonly TokenStream tokens;
    private int nestingDepth;

    public ExpressionParser(TokenStream tokens)
    {
        this.tokens = tokens;
    }

    public Expression ParseExpression()
    {
        var expression = ParseOr();
        if (ExpressionTree.DepthUpTo(expression, MaximumExpressionDepth) > MaximumExpressionDepth)
        {
            throw new SqlSyntaxException("expression is too deeply nested", tokens.Current.Start);
        }

        return expression;
    }

    private Expression ParseOr()
    {
        var left = ParseAnd();
        while (tokens.MatchKeyword("OR"))
        {
            left = new BinaryExpression(BinaryOperator.Or, left, ParseAnd());
        }

        return left;
    }

    private Expression ParseAnd()
    {
        var left = ParseNot();
        while (tokens.MatchKeyword("AND"))
        {
            left = new BinaryExpression(BinaryOperator.And, left, ParseNot());
        }

        return left;
    }

    private Expression ParseNot()
    {
        if (!tokens.MatchKeyword("NOT"))
        {
            return ParseIsNull();
        }

        return new UnaryExpression(UnaryOperator.Not, ParseNested(ParseNot));
    }

    private Expression ParseIsNull()
    {
        var operand = ParseComparison();
        while (tokens.MatchKeyword("IS"))
        {
            var isNegated = tokens.MatchKeyword("NOT");
            tokens.ExpectKeyword("NULL");
            operand = new IsNullExpression(operand, isNegated);
        }

        return operand;
    }

    private Expression ParseComparison() => ParseLeftAssociative(ParseConcatenation, ComparisonOperators);

    private Expression ParseConcatenation() => ParseLeftAssociative(ParseAdditive, ConcatenationOperators);

    private Expression ParseAdditive() => ParseLeftAssociative(ParseMultiplicative, AdditiveOperators);

    private Expression ParseMultiplicative() => ParseLeftAssociative(ParseUnary, MultiplicativeOperators);

    private Expression ParseLeftAssociative(
        Func<Expression> parseOperand,
        IReadOnlyDictionary<string, BinaryOperator> operators)
    {
        var left = parseOperand();
        while (tokens.Current.Kind == TokenKind.Symbol
               && operators.TryGetValue(tokens.Current.Text, out var currentOperator))
        {
            tokens.Advance();
            left = new BinaryExpression(currentOperator, left, parseOperand());
        }

        return left;
    }

    private Expression ParseUnary()
    {
        if (tokens.MatchSymbol(PlusSymbol))
        {
            return new UnaryExpression(UnaryOperator.Plus, ParseNested(ParseUnary));
        }

        if (!tokens.MatchSymbol(NegationSymbol))
        {
            return ParsePrimary();
        }

        // Folding the sign into the literal lets -9223372036854775808 parse, which a separate negate cannot.
        if (tokens.Current.Kind == TokenKind.IntegerLiteral)
        {
            return new LiteralExpression(ParseInteger(tokens.Advance(), isNegative: true));
        }

        return new UnaryExpression(UnaryOperator.Negate, ParseNested(ParseUnary));
    }

    private Expression ParsePrimary()
    {
        var token = tokens.Current;

        if (token.Kind == TokenKind.IntegerLiteral)
        {
            tokens.Advance();
            return new LiteralExpression(ParseInteger(token, isNegative: false));
        }

        if (token.Kind == TokenKind.RealLiteral)
        {
            tokens.Advance();
            return new LiteralExpression(ParseReal(token));
        }

        if (token.Kind == TokenKind.StringLiteral)
        {
            tokens.Advance();
            return new LiteralExpression(token.Text);
        }

        if (token.Kind == TokenKind.Identifier)
        {
            return ParseColumnOrFunction();
        }

        if (token.Kind == TokenKind.Keyword)
        {
            return ParseKeywordLiteral();
        }

        if (tokens.MatchSymbol("("))
        {
            var inner = ParseNested(ParseOr);
            tokens.ExpectSymbol(")");
            return inner;
        }

        throw tokens.Unexpected("expected an expression");
    }

    private Expression ParseKeywordLiteral()
    {
        if (tokens.MatchKeyword("TRUE"))
        {
            return new LiteralExpression(true);
        }

        if (tokens.MatchKeyword("FALSE"))
        {
            return new LiteralExpression(false);
        }

        if (tokens.MatchKeyword("NULL"))
        {
            return new LiteralExpression(null);
        }

        throw tokens.Unexpected("expected an expression");
    }

    private Expression ParseColumnOrFunction()
    {
        var name = tokens.Advance().Text;

        if (tokens.MatchSymbol("("))
        {
            return ParseFunctionCall(name);
        }

        if (tokens.MatchSymbol("."))
        {
            return new ColumnReference(name, tokens.ExpectIdentifier("column name"));
        }

        return new ColumnReference(null, name);
    }

    private Expression ParseFunctionCall(string name)
    {
        if (tokens.MatchSymbol("*"))
        {
            tokens.ExpectSymbol(")");
            return new FunctionCall(name, [], IsWildcardArgument: true);
        }

        var arguments = new List<Expression>();
        if (!tokens.MatchSymbol(")"))
        {
            arguments.Add(ParseNested(ParseOr));
            while (tokens.MatchSymbol(","))
            {
                arguments.Add(ParseNested(ParseOr));
            }

            tokens.ExpectSymbol(")");
        }

        return new FunctionCall(name, arguments, IsWildcardArgument: false);
    }

    private Expression ParseNested(Func<Expression> parse)
    {
        nestingDepth++;
        try
        {
            if (nestingDepth > MaximumNestingDepth)
            {
                throw new SqlSyntaxException("expression is nested too deeply", tokens.Current.Start);
            }

            return parse();
        }
        finally
        {
            nestingDepth--;
        }
    }

    private static long ParseInteger(Token token, bool isNegative)
    {
        var text = isNegative ? NegationSymbol + token.Text : token.Text;
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw new SqlSyntaxException("integer literal is out of range", token.Start);
        }

        return value;
    }

    private static double ParseReal(Token token)
    {
        if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw new SqlSyntaxException("real literal is out of range", token.Start);
        }

        return value;
    }
}
