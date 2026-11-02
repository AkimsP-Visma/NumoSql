namespace NumoSql.Lexing;

internal enum TokenKind
{
    Keyword,
    Identifier,
    IntegerLiteral,
    RealLiteral,
    StringLiteral,
    Symbol,
    EndOfInput,
}
