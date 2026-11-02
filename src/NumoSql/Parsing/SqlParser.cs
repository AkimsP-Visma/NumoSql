using NumoSql.Lexing;

namespace NumoSql.Parsing;

internal static class SqlParser
{
    public static IReadOnlyList<ParsedStatement> ParseScript(string script)
    {
        var tokens = new TokenStream(Lexer.Tokenize(script));
        var statementParser = new StatementParser(tokens);
        var statements = new List<ParsedStatement>();

        while (!tokens.IsEndOfInput)
        {
            if (tokens.MatchSymbol(";"))
            {
                continue;
            }

            var start = tokens.Current.Start;
            var statement = statementParser.ParseStatement();
            statements.Add(new ParsedStatement(statement, script[start..tokens.LastConsumedEnd]));

            if (!tokens.IsEndOfInput)
            {
                tokens.ExpectSymbol(";");
            }
        }

        return statements;
    }
}
