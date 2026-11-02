using NumoSql.Lexing;
using Xunit;

namespace NumoSql.Tests;

public sealed class LexerTests
{
    [Fact]
    public void Unquoted_words_fold_case_and_keywords_are_marked()
    {
        var tokens = Lexer.Tokenize("select Name FROM t");

        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("SELECT", tokens[0].Text);
        Assert.Equal(TokenKind.Identifier, tokens[1].Kind);
        Assert.Equal("name", tokens[1].Text);
        Assert.Equal("FROM", tokens[2].Text);
        Assert.Equal(TokenKind.EndOfInput, tokens[^1].Kind);
    }

    [Fact]
    public void Quoted_identifiers_keep_case_and_unescape_doubled_quotes()
    {
        var tokens = Lexer.Tokenize("\"Mixed \"\"Case\"\"\"");

        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
        Assert.Equal("Mixed \"Case\"", tokens[0].Text);
    }

    [Fact]
    public void String_literals_unescape_doubled_quotes()
    {
        var tokens = Lexer.Tokenize("'it''s'");

        Assert.Equal(TokenKind.StringLiteral, tokens[0].Kind);
        Assert.Equal("it's", tokens[0].Text);
    }

    [Fact]
    public void Numbers_are_classified_as_integer_or_real()
    {
        var tokens = Lexer.Tokenize("1 2.5 1e3 3E-2 7.0e+2");

        Assert.Equal(
            new[] { TokenKind.IntegerLiteral, TokenKind.RealLiteral, TokenKind.RealLiteral, TokenKind.RealLiteral, TokenKind.RealLiteral },
            tokens.Take(5).Select(token => token.Kind));
    }

    [Fact]
    public void Line_and_block_comments_are_skipped()
    {
        var tokens = Lexer.Tokenize("SELECT -- trailing\n/* block */ 1");

        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal(TokenKind.IntegerLiteral, tokens[1].Kind);
        Assert.Equal("1", tokens[1].Text);
    }

    [Fact]
    public void Unterminated_string_is_a_syntax_error()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => Lexer.Tokenize("SELECT 'open"));
    }

    [Fact]
    public void Unknown_character_is_a_syntax_error()
    {
        Assert.ThrowsAny<SqlSyntaxException>(() => Lexer.Tokenize("SELECT 1 ! 2"));
    }
}
