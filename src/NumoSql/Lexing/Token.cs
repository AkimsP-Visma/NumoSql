namespace NumoSql.Lexing;

/// <summary>
/// A lexical unit. Keyword text is upper case, unquoted identifiers are lower case, and quoted identifiers
/// and string literals hold their unescaped content. Start and End are offsets into the source text.
/// </summary>
internal sealed record Token(TokenKind Kind, string Text, int Start, int End);
