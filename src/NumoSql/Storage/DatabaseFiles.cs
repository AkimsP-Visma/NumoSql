namespace NumoSql.Storage;

internal sealed record DatabaseFiles(string Root)
{
    private const string WalFileName = "numosql.wal";
    private const string CheckpointFileName = "numosql.checkpoint";

    public string WalPath => Path.Combine(Root, WalFileName);

    public string CheckpointPath => Path.Combine(Root, CheckpointFileName);
}
