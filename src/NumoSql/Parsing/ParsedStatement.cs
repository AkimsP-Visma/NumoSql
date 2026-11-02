using NumoSql.Syntax;

namespace NumoSql.Parsing;

/// <param name="SourceText">The statement's exact text. The write-ahead log stores this, so recovery re-parses it.</param>
internal sealed record ParsedStatement(Statement Statement, string SourceText);
