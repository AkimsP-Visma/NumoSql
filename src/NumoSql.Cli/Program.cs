using System.Text;
using NumoSql;

const string DefaultDirectory = "numosql-data";
const string QuitCommand = "\\q";
const string PrimaryPrompt = "numosql> ";
const string ContinuationPrompt = "     -> ";

var directory = args.Length > 0 ? args[0] : DefaultDirectory;
using var database = Database.Open(directory);
using var session = database.CreateSession();

Console.WriteLine($"NUMOSQL shell on {Path.GetFullPath(directory)}. End each statement with ';'. {QuitCommand} quits.");

var pending = new StringBuilder();
while (true)
{
    Console.Write(pending.Length == 0 ? PrimaryPrompt : ContinuationPrompt);
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    if (pending.Length == 0 && line.Trim() == QuitCommand)
    {
        break;
    }

    pending.AppendLine(line);
    if (!line.TrimEnd().EndsWith(';'))
    {
        continue;
    }

    var script = pending.ToString();
    pending.Clear();
    RunScript(session, script);
}

static void RunScript(Session session, string script)
{
    try
    {
        foreach (var result in session.Execute(script))
        {
            Console.Write(ResultFormatter.Format(result));
        }
    }
    catch (NumoSqlException failure)
    {
        Console.Error.WriteLine($"ERROR: {failure.Message}");
    }
}
