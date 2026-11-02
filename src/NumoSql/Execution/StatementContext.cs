using NumoSql.Schema;
using NumoSql.Transactions;

namespace NumoSql.Execution;

/// <summary>What a statement needs: the committed tables it reads and writes, and the transaction that records how to undo each change.</summary>
internal sealed record StatementContext(Catalog Catalog, Transaction Transaction);
