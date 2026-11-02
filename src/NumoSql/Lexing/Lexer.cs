using System.Globalization;
using System.Text;

namespace NumoSql.Lexing;

internal sealed class Lexer
{
    /// <summary>
    /// Words that cannot be used as bare identifiers. Contextual words such as KEY, TABLES and TRANSACTION
    /// stay identifiers so that columns named after them still work.
    /// </summary>
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "AND", "AS", "ASC", "BEGIN", "BY", "CHECKPOINT", "COMMIT", "CREATE", "CROSS", "DELETE", "DESC",
        "DISTINCT", "DROP", "EXCEPT", "EXISTS", "FALSE", "FROM", "FULL", "GROUP", "HAVING", "IF", "INNER",
        "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "LEFT", "LIMIT", "NOT", "NULL", "OFFSET", "ON", "OR",
        "ORDER", "OUTER", "PRIMARY", "RIGHT", "ROLLBACK", "SELECT", "SET", "SHOW", "TABLE", "TRUE", "UNION",
        "UPDATE", "VALUES", "WHERE",
    };

    private static readonly string[] TwoCharacterSymbols = ["<>", "!=", "<=", ">=", "||"];
    private const string SingleCharacterSymbols = "(),.;*+-/=<>";
    private const char StringQuote = '\'';
    private const char IdentifierQuote = '"';
    private const char ExponentMarkerLower = 'e';
    private const char ExponentMarkerUpper = 'E';
    private const char DecimalPoint = '.';
    private const char ExponentSignPlus = '+';
    private const char ExponentSignMinus = '-';
    private const string LineCommentStart = "--";
    private static readonly char[] LineTerminators = ['\n', '\r'];
    private const string BlockCommentStart = "/*";
    private const string BlockCommentEnd = "*/";

    private readonly string sql;
    private readonly List<Token> tokens = [];
    private int position;

    private Lexer(string sql)
    {
        this.sql = sql;
    }

    public static IReadOnlyList<Token> Tokenize(string sql)
    {
        var lexer = new Lexer(sql);
        lexer.ReadAllTokens();
        return lexer.tokens;
    }

    private void ReadAllTokens()
    {
        while (true)
        {
            SkipWhitespaceAndComments();
            if (position >= sql.Length)
            {
                tokens.Add(new Token(TokenKind.EndOfInput, string.Empty, sql.Length, sql.Length));
                return;
            }

            tokens.Add(ReadToken());
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (position < sql.Length)
        {
            if (char.IsWhiteSpace(sql[position]))
            {
                position++;
            }
            else if (StartsWith(LineCommentStart))
            {
                SkipLineComment();
            }
            else if (StartsWith(BlockCommentStart))
            {
                SkipBlockComment();
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Ends at CR or LF, as PostgreSQL does, so text after a lone CR is not swallowed by the comment.</summary>
    private void SkipLineComment()
    {
        var terminatorIndex = sql.IndexOfAny(LineTerminators, position);
        position = terminatorIndex < 0 ? sql.Length : terminatorIndex + 1;
    }

    private void SkipBlockComment()
    {
        var start = position;
        var endIndex = sql.IndexOf(BlockCommentEnd, position + BlockCommentStart.Length, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            throw new SqlSyntaxException("unterminated /* comment", start);
        }

        position = endIndex + BlockCommentEnd.Length;
    }

    private Token ReadToken()
    {
        var current = sql[position];
        if (char.IsLetter(current) || current == '_')
        {
            return ReadWord();
        }

        if (char.IsAsciiDigit(current) || (current == DecimalPoint && IsAsciiDigitAt(position + 1)))
        {
            return ReadNumber();
        }

        if (current == StringQuote)
        {
            return ReadQuoted(TokenKind.StringLiteral, StringQuote);
        }

        if (current == IdentifierQuote)
        {
            return ReadQuoted(TokenKind.Identifier, IdentifierQuote);
        }

        return ReadSymbol();
    }

    private Token ReadWord()
    {
        var start = position;
        while (position < sql.Length && IsWordCharacter(sql[position]))
        {
            position++;
        }

        var word = sql[start..position];
        var upperWord = word.ToUpperInvariant();
        return ReservedWords.Contains(upperWord)
            ? new Token(TokenKind.Keyword, upperWord, start, position)
            : new Token(TokenKind.Identifier, word.ToLowerInvariant(), start, position);
    }

    private static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_' || IsCombiningMark(character);

    /// <summary>
    /// Marks extend a word in PostgreSQL, for example an accent typed as a separate code point. Supplementary-plane
    /// letters are still rejected, because the lexer works one UTF-16 unit at a time.
    /// </summary>
    private static bool IsCombiningMark(char character) => char.GetUnicodeCategory(character) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.ConnectorPunctuation;

    private Token ReadNumber()
    {
        var start = position;
        var isReal = false;

        if (sql[position] == DecimalPoint)
        {
            isReal = true;
            position++;
            SkipDigits();
        }
        else
        {
            SkipDigits();
            if (position < sql.Length && sql[position] == DecimalPoint)
            {
                isReal = true;
                position++;
                SkipDigits();
            }
        }

        if (TryReadExponent())
        {
            isReal = true;
        }

        // A word right after a number would otherwise become an alias in a select list, as in SELECT 1abc.
        if (position < sql.Length && IsWordCharacter(sql[position]))
        {
            throw new SqlSyntaxException("a numeric literal cannot be followed by a letter, digit or underscore", position);
        }

        var kind = isReal ? TokenKind.RealLiteral : TokenKind.IntegerLiteral;
        return new Token(kind, sql[start..position], start, position);
    }

    private bool IsAsciiDigitAt(int index) => index < sql.Length && char.IsAsciiDigit(sql[index]);

    private void SkipDigits()
    {
        while (position < sql.Length && char.IsAsciiDigit(sql[position]))
        {
            position++;
        }
    }

    private bool TryReadExponent()
    {
        if (position >= sql.Length || sql[position] is not (ExponentMarkerLower or ExponentMarkerUpper))
        {
            return false;
        }

        var digitsStart = position + 1;
        if (digitsStart < sql.Length && sql[digitsStart] is ExponentSignPlus or ExponentSignMinus)
        {
            digitsStart++;
        }

        if (digitsStart >= sql.Length || !char.IsAsciiDigit(sql[digitsStart]))
        {
            return false;
        }

        position = digitsStart;
        SkipDigits();
        return true;
    }

    /// <summary>Reads a string literal or quoted identifier. A doubled quote character stands for one quote.</summary>
    private Token ReadQuoted(TokenKind kind, char quote)
    {
        var start = position;
        position++;
        var content = new StringBuilder();

        while (true)
        {
            if (position >= sql.Length)
            {
                var description = kind == TokenKind.StringLiteral ? "unterminated string literal" : "unterminated quoted identifier";
                throw new SqlSyntaxException(description, start);
            }

            var current = sql[position];
            position++;

            if (char.IsSurrogate(current))
            {
                AppendSurrogatePair(content, current);
                continue;
            }

            if (current != quote)
            {
                content.Append(current);
                continue;
            }

            if (position < sql.Length && sql[position] == quote)
            {
                content.Append(quote);
                position++;
                continue;
            }

            break;
        }

        if (kind == TokenKind.Identifier && content.Length == 0)
        {
            throw new SqlSyntaxException("zero-length delimited identifier", start);
        }

        return new Token(kind, content.ToString(), start, position);
    }

    /// <summary>
    /// Keeps a surrogate pair together and rejects a lone surrogate. A lone surrogate has no UTF-8 encoding, so the
    /// log would silently replace it and replay would rebuild different data.
    /// </summary>
    private void AppendSurrogatePair(StringBuilder content, char first)
    {
        if (!char.IsHighSurrogate(first) || position >= sql.Length || !char.IsLowSurrogate(sql[position]))
        {
            throw new SqlSyntaxException("text contains an unpaired UTF-16 surrogate", position - 1);
        }

        content.Append(first).Append(sql[position]);
        position++;
    }

    private Token ReadSymbol()
    {
        var start = position;
        foreach (var symbol in TwoCharacterSymbols)
        {
            if (StartsWith(symbol))
            {
                position += symbol.Length;
                return new Token(TokenKind.Symbol, symbol, start, position);
            }
        }

        if (SingleCharacterSymbols.Contains(sql[position]))
        {
            position++;
            return new Token(TokenKind.Symbol, sql[start..position], start, position);
        }

        throw new SqlSyntaxException($"unexpected character '{sql[position]}'", position);
    }

    private bool StartsWith(string text) => sql.AsSpan(position).StartsWith(text, StringComparison.Ordinal);
}
