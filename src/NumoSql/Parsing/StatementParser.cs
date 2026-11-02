using System.Globalization;
using NumoSql.Lexing;
using NumoSql.Syntax;
using NumoSql.Types;

namespace NumoSql.Parsing;

internal sealed class StatementParser
{
    private static readonly IReadOnlyDictionary<string, SqlType> ColumnTypeNames =
        new Dictionary<string, SqlType>(StringComparer.Ordinal)
        {
            ["integer"] = SqlType.Integer,
            ["int"] = SqlType.Integer,
            ["real"] = SqlType.Real,
            ["text"] = SqlType.Text,
            ["boolean"] = SqlType.Boolean,
        };

    /// <summary>Clauses the grammar does not implement. Naming them gives a clearer error than a generic one.</summary>
    private static readonly string[] UnsupportedTrailingKeywords = ["HAVING", "UNION", "EXCEPT", "INTERSECT"];

    private const string PrimaryKeyWord = "key";
    private const string TransactionWord = "transaction";
    private const string TablesWord = "tables";

    private readonly TokenStream tokens;
    private readonly ExpressionParser expressions;

    public StatementParser(TokenStream tokens)
    {
        this.tokens = tokens;
        expressions = new ExpressionParser(tokens);
    }

    public Statement ParseStatement()
    {
        if (tokens.MatchKeyword("CREATE"))
        {
            return ParseCreateTable();
        }

        if (tokens.MatchKeyword("DROP"))
        {
            return ParseDropTable();
        }

        if (tokens.MatchKeyword("INSERT"))
        {
            return ParseInsert();
        }

        if (tokens.MatchKeyword("UPDATE"))
        {
            return ParseUpdate();
        }

        if (tokens.MatchKeyword("DELETE"))
        {
            return ParseDelete();
        }

        if (tokens.MatchKeyword("SELECT"))
        {
            return ParseSelect();
        }

        if (tokens.MatchKeyword("SHOW"))
        {
            tokens.ExpectWord(TablesWord);
            return new ShowTablesStatement();
        }

        return ParseTransactionControl();
    }

    private Statement ParseTransactionControl()
    {
        if (tokens.MatchKeyword("BEGIN"))
        {
            tokens.MatchWord(TransactionWord);
            return new BeginStatement();
        }

        if (tokens.MatchKeyword("COMMIT"))
        {
            tokens.MatchWord(TransactionWord);
            return new CommitStatement();
        }

        if (tokens.MatchKeyword("ROLLBACK"))
        {
            tokens.MatchWord(TransactionWord);
            return new RollbackStatement();
        }

        if (tokens.MatchKeyword("CHECKPOINT"))
        {
            return new CheckpointStatement();
        }

        throw tokens.Unexpected("expected a statement");
    }

    private Statement ParseCreateTable()
    {
        tokens.ExpectKeyword("TABLE");
        var ifNotExists = MatchIfNotExists();
        var tableName = tokens.ExpectIdentifier("table name");

        tokens.ExpectSymbol("(");
        var columns = new List<ColumnSpecification>();
        string? tablePrimaryKeyColumn = null;
        do
        {
            if (tokens.MatchKeyword("PRIMARY"))
            {
                tablePrimaryKeyColumn = ParseTablePrimaryKeyColumn();
            }
            else
            {
                columns.Add(ParseColumnDefinition());
            }
        }
        while (tokens.MatchSymbol(","));

        tokens.ExpectSymbol(")");

        if (tablePrimaryKeyColumn is not null)
        {
            MarkPrimaryKeyColumn(columns, tablePrimaryKeyColumn);
        }

        if (columns.Count(column => column.IsPrimaryKey) > 1)
        {
            throw new SqlSyntaxException("a table can have only one primary key", tokens.Current.Start);
        }

        return new CreateTableStatement(tableName, ifNotExists, columns);
    }

    /// <summary>Parses the table constraint PRIMARY KEY ( column ). Composite keys are not supported.</summary>
    private string ParseTablePrimaryKeyColumn()
    {
        tokens.ExpectWord(PrimaryKeyWord);
        tokens.ExpectSymbol("(");
        var columnName = tokens.ExpectIdentifier("column name");
        tokens.ExpectSymbol(")");
        return columnName;
    }

    private void MarkPrimaryKeyColumn(List<ColumnSpecification> columns, string columnName)
    {
        var index = columns.FindIndex(column => column.Name == columnName);
        if (index < 0)
        {
            throw new SqlSyntaxException($"primary key column \"{columnName}\" does not exist", tokens.Current.Start);
        }

        columns[index] = columns[index] with { IsPrimaryKey = true, IsNotNull = true };
    }

    private bool MatchIfNotExists()
    {
        if (!tokens.MatchKeyword("IF"))
        {
            return false;
        }

        tokens.ExpectKeyword("NOT");
        tokens.ExpectKeyword("EXISTS");
        return true;
    }

    private ColumnSpecification ParseColumnDefinition()
    {
        var name = tokens.ExpectIdentifier("column name");
        var type = ParseColumnType();
        var isNotNull = false;
        var isPrimaryKey = false;

        while (true)
        {
            if (tokens.MatchKeyword("NOT"))
            {
                tokens.ExpectKeyword("NULL");
                isNotNull = true;
            }
            else if (tokens.MatchKeyword("PRIMARY"))
            {
                tokens.ExpectWord(PrimaryKeyWord);
                isPrimaryKey = true;
            }
            else
            {
                break;
            }
        }

        return new ColumnSpecification(name, type, isNotNull || isPrimaryKey, isPrimaryKey);
    }

    private SqlType ParseColumnType()
    {
        if (tokens.Current.Kind == TokenKind.Identifier && ColumnTypeNames.TryGetValue(tokens.Current.Text, out var type))
        {
            tokens.Advance();
            return type;
        }

        throw tokens.Unexpected("expected a column type (integer, real, text or boolean)");
    }

    private Statement ParseDropTable()
    {
        tokens.ExpectKeyword("TABLE");
        var ifExists = tokens.MatchKeyword("IF");
        if (ifExists)
        {
            tokens.ExpectKeyword("EXISTS");
        }

        return new DropTableStatement(tokens.ExpectIdentifier("table name"), ifExists);
    }

    private Statement ParseInsert()
    {
        tokens.ExpectKeyword("INTO");
        var tableName = tokens.ExpectIdentifier("table name");
        var columnNames = tokens.IsSymbol("(") ? ParseParenthesizedNames() : null;

        tokens.ExpectKeyword("VALUES");
        var rows = new List<IReadOnlyList<Expression>> { ParseValueRow() };
        while (tokens.MatchSymbol(","))
        {
            rows.Add(ParseValueRow());
        }

        return new InsertStatement(tableName, columnNames, rows);
    }

    private List<string> ParseParenthesizedNames()
    {
        tokens.ExpectSymbol("(");
        var names = new List<string> { tokens.ExpectIdentifier("column name") };
        while (tokens.MatchSymbol(","))
        {
            names.Add(tokens.ExpectIdentifier("column name"));
        }

        tokens.ExpectSymbol(")");
        return names;
    }

    private List<Expression> ParseValueRow()
    {
        tokens.ExpectSymbol("(");
        var values = new List<Expression> { expressions.ParseExpression() };
        while (tokens.MatchSymbol(","))
        {
            values.Add(expressions.ParseExpression());
        }

        tokens.ExpectSymbol(")");
        return values;
    }

    private Statement ParseUpdate()
    {
        var tableName = tokens.ExpectIdentifier("table name");
        tokens.ExpectKeyword("SET");

        var assignments = new List<Assignment> { ParseAssignment() };
        while (tokens.MatchSymbol(","))
        {
            assignments.Add(ParseAssignment());
        }

        return new UpdateStatement(tableName, assignments, ParseOptionalWhere());
    }

    private Assignment ParseAssignment()
    {
        var columnName = tokens.ExpectIdentifier("column name");
        tokens.ExpectSymbol("=");
        return new Assignment(columnName, expressions.ParseExpression());
    }

    private Statement ParseDelete()
    {
        tokens.ExpectKeyword("FROM");
        var tableName = tokens.ExpectIdentifier("table name");
        return new DeleteStatement(tableName, ParseOptionalWhere());
    }

    private Statement ParseSelect()
    {
        RejectUnsupported("DISTINCT");
        var items = ParseSelectItems();

        TableReference? from = null;
        var joins = new List<JoinClause>();
        if (tokens.MatchKeyword("FROM"))
        {
            from = ParseTableReference();
            joins = ParseJoins();
            if (tokens.IsSymbol(","))
            {
                throw new SqlSyntaxException("comma-separated table lists are not supported, use INNER JOIN", tokens.Current.Start);
            }
        }

        var condition = ParseOptionalWhere();
        var groupBy = ParseGroupBy();
        var orderBy = ParseOrderBy();
        var (limit, offset) = ParseLimitAndOffset();
        RejectUnsupportedTrailingClauses();

        return new SelectStatement(items, from, joins, condition, groupBy, orderBy, limit, offset);
    }

    private List<SelectItem> ParseSelectItems()
    {
        var items = new List<SelectItem> { ParseSelectItem() };
        while (tokens.MatchSymbol(","))
        {
            items.Add(ParseSelectItem());
        }

        return items;
    }

    private SelectItem ParseSelectItem()
    {
        if (tokens.MatchSymbol("*"))
        {
            return new SelectItem(new WildcardExpression(), null);
        }

        var value = expressions.ParseExpression();
        return new SelectItem(value, ParseOptionalAlias());
    }

    private TableReference ParseTableReference()
    {
        var name = tokens.ExpectIdentifier("table name");
        return new TableReference(name, ParseOptionalAlias());
    }

    private string? ParseOptionalAlias()
    {
        if (tokens.MatchKeyword("AS"))
        {
            return tokens.ExpectIdentifier("alias name");
        }

        return tokens.Current.Kind == TokenKind.Identifier ? tokens.Advance().Text : null;
    }

    private List<JoinClause> ParseJoins()
    {
        var joins = new List<JoinClause>();
        while (MatchJoinKeyword())
        {
            var table = ParseTableReference();
            tokens.ExpectKeyword("ON");
            joins.Add(new JoinClause(table, expressions.ParseExpression()));
        }

        return joins;
    }

    /// <summary>Consumes the JOIN keyword of an inner join. Outer and cross joins are rejected, not silently treated as inner.</summary>
    private bool MatchJoinKeyword()
    {
        if (tokens.MatchKeyword("INNER"))
        {
            tokens.ExpectKeyword("JOIN");
            return true;
        }

        if (tokens.MatchKeyword("JOIN"))
        {
            return true;
        }

        if (tokens.IsKeyword("LEFT") || tokens.IsKeyword("RIGHT") || tokens.IsKeyword("FULL")
            || tokens.IsKeyword("CROSS") || tokens.IsKeyword("OUTER"))
        {
            throw new SqlSyntaxException("outer and cross joins are not supported, use INNER JOIN", tokens.Current.Start);
        }

        return false;
    }

    private Expression? ParseOptionalWhere() => tokens.MatchKeyword("WHERE") ? expressions.ParseExpression() : null;

    private List<Expression> ParseGroupBy()
    {
        var keys = new List<Expression>();
        if (!tokens.MatchKeyword("GROUP"))
        {
            return keys;
        }

        tokens.ExpectKeyword("BY");
        keys.Add(expressions.ParseExpression());
        while (tokens.MatchSymbol(","))
        {
            keys.Add(expressions.ParseExpression());
        }

        return keys;
    }

    private List<OrderItem> ParseOrderBy()
    {
        var items = new List<OrderItem>();
        if (!tokens.MatchKeyword("ORDER"))
        {
            return items;
        }

        tokens.ExpectKeyword("BY");
        items.Add(ParseOrderItem());
        while (tokens.MatchSymbol(","))
        {
            items.Add(ParseOrderItem());
        }

        return items;
    }

    private OrderItem ParseOrderItem()
    {
        var start = tokens.Current.Start;
        var value = expressions.ParseExpression();

        // ORDER BY 1 would sort by a constant, silently ignoring the intended column.
        if (value is LiteralExpression { Value: long })
        {
            throw new SqlSyntaxException("ORDER BY position numbers are not supported, use a column name or alias", start);
        }

        var isDescending = tokens.MatchKeyword("DESC");
        if (!isDescending)
        {
            tokens.MatchKeyword("ASC");
        }

        return new OrderItem(value, isDescending);
    }

    private (long? Limit, long? Offset) ParseLimitAndOffset()
    {
        long? limit = null;
        long? offset = null;

        while (true)
        {
            if (limit is null && tokens.MatchKeyword("LIMIT"))
            {
                limit = ParseNonNegativeCount("LIMIT");
            }
            else if (offset is null && tokens.MatchKeyword("OFFSET"))
            {
                offset = ParseNonNegativeCount("OFFSET");
            }
            else
            {
                return (limit, offset);
            }
        }
    }

    private long ParseNonNegativeCount(string clauseName)
    {
        var token = tokens.Current;
        if (token.Kind != TokenKind.IntegerLiteral
            || !long.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            throw tokens.Unexpected($"expected a non-negative integer after {clauseName}");
        }

        tokens.Advance();
        return count;
    }

    private void RejectUnsupported(string keyword)
    {
        if (tokens.IsKeyword(keyword))
        {
            throw new SqlSyntaxException($"{keyword} is not supported", tokens.Current.Start);
        }
    }

    private void RejectUnsupportedTrailingClauses()
    {
        foreach (var keyword in UnsupportedTrailingKeywords)
        {
            RejectUnsupported(keyword);
        }
    }
}
