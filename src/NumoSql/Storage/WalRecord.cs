namespace NumoSql.Storage;

/// <summary>One committed transaction: its sequence number and the source text of each data-changing statement.</summary>
internal sealed record WalRecord(long LogSequenceNumber, IReadOnlyList<string> Statements);
