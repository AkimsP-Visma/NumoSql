using NumoSql.Types;

namespace NumoSql.Schema;

internal sealed record ColumnDefinition(string Name, SqlType Type, bool IsNotNull);
