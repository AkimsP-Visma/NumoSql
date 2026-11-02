using System.Diagnostics;
using NumoSql;

const int BulkRowCount = 200_000;
const int AutocommitRowCount = 2_000;
const int PointLookupCount = 20;
const string ScanQuery = "SELECT COUNT(*), SUM(amount) FROM t WHERE amount > 10;";
const string TableScript = "CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, amount INTEGER);";

var directory = Path.Combine(Path.GetTempPath(), "numosql-bench", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);

using (var database = Database.Open(directory))
using (var session = database.CreateSession())
{
    session.Execute(TableScript);
    var values = string.Join(",", Enumerable.Range(1, BulkRowCount).Select(index => $"({index}, 'name{index}', {index % 1000})"));
    Report("bulk insert, one transaction", () => session.Execute($"BEGIN; INSERT INTO t VALUES {values}; COMMIT;"));
    Report("autocommit inserts, one commit each", () =>
    {
        for (var index = 1; index <= AutocommitRowCount; index++)
        {
            session.Execute($"INSERT INTO t VALUES ({BulkRowCount + index}, 'x', 1);");
        }
    });
}

Report("reopen, replaying the log", () => { using var reopened = Database.Open(directory); });

using (var database = Database.Open(directory))
using (var session = database.CreateSession())
{
    Report("full scan with aggregate", () => session.Execute(ScanQuery));
    Report($"{PointLookupCount} point lookups by primary key", () =>
    {
        for (var index = 1; index <= PointLookupCount; index++)
        {
            session.Execute($"SELECT name FROM t WHERE id = {index * 1000};");
        }
    });
    Report("checkpoint", () => session.Execute("CHECKPOINT;"));
}

Report("reopen, loading the checkpoint", () => { using var reopened = Database.Open(directory); });

Directory.Delete(directory, recursive: true);

static void Report(string label, Action action)
{
    var stopwatch = Stopwatch.StartNew();
    action();
    stopwatch.Stop();
    Console.WriteLine($"{label,-42} {stopwatch.Elapsed.TotalMilliseconds,12:F1} ms");
}
