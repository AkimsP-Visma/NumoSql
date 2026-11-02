using NumoSql.Lexing;

namespace NumoSql.Parsing;

internal sealed class TokenStream
{
    private readonly IReadOnlyList<Token> tokens;
    private int index;

    public TokenStream(IReadOnlyList<Token> tokens)
    {
        this.tokens = tokens;
    }

    public Token Current => tokens[index];

    public bool IsEndOfInput => Current.Kind == TokenKind.EndOfInput;

    /// <summary>End offset of the last consumed token, which bounds the text of the statement just parsed.</summary>
    public int LastConsumedEnd => index == 0 ? 0 : tokens[index - 1].End;

    public Token Advance()
    {
        var token = tokens[index];
        if (token.Kind != TokenKind.EndOfInput)
        {
            index++;
        }

        return token;
    }

    public bool IsKeyword(string keyword) => Current.Kind == TokenKind.Keyword && Current.Text == keyword;

    public bool IsSymbol(string symbol) => Current.Kind == TokenKind.Symbol && Current.Text == symbol;

    public bool IsWord(string word) => Current.Kind == TokenKind.Identifier && Current.Text == word;

    public bool MatchKeyword(string keyword) => MatchWhen(IsKeyword(keyword));

    public bool MatchSymbol(string symbol) => MatchWhen(IsSymbol(symbol));

    public bool MatchWord(string word) => MatchWhen(IsWord(word));

    public void ExpectKeyword(string keyword)
    {
        if (!MatchKeyword(keyword))
        {
            throw Unexpected($"expected {keyword}");
        }
    }

    public void ExpectSymbol(string symbol)
    {
        if (!MatchSymbol(symbol))
        {
            throw Unexpected($"expected '{symbol}'");
        }
    }

    public void ExpectWord(string word)
    {
        if (!MatchWord(word))
        {
            throw Unexpected($"expected {word.ToUpperInvariant()}");
        }
    }

    public string ExpectIdentifier(string description)
    {
        if (Current.Kind != TokenKind.Identifier)
        {
            throw Unexpected($"expected {description}");
        }

        return Advance().Text;
    }

    public SqlSyntaxException Unexpected(string expectation)
    {
        var found = Current.Kind == TokenKind.EndOfInput ? "end of input" : $"\"{Current.Text}\"";
        return new SqlSyntaxException($"{expectation}, found {found}", Current.Start);
    }

    private bool MatchWhen(bool condition)
    {
        if (condition)
        {
            Advance();
        }

        return condition;
    }
}
