namespace NumoSql.Transactions;

/// <summary>
/// The changes of one transaction. Writes go straight into the committed state, which is safe because the write
/// gate keeps every other session out until the transaction ends. Each change registers the action that reverses
/// it, so rollback restores the starting state without copying any table.
/// </summary>
internal sealed class Transaction
{
    private readonly List<Action> undoActions = [];
    private readonly List<string> loggedStatements = [];

    /// <summary>Source text of each data-changing statement that succeeded, in order. This is what the log stores.</summary>
    public IReadOnlyList<string> LoggedStatements => loggedStatements;

    /// <summary>An error inside an explicit transaction aborts it. Only ROLLBACK or COMMIT ends an aborted transaction.</summary>
    public bool IsAborted { get; private set; }

    public void RecordUndo(Action undo)
    {
        undoActions.Add(undo);
    }

    public void RecordStatement(string sourceText)
    {
        loggedStatements.Add(sourceText);
    }

    public void MarkAborted()
    {
        IsAborted = true;
    }

    /// <summary>Reverses every change, newest first, and forgets the logged statements.</summary>
    public void Rollback()
    {
        for (var index = undoActions.Count - 1; index >= 0; index--)
        {
            undoActions[index]();
        }

        Complete();
    }

    /// <summary>Forgets the undo actions and logged statements. Call this once the changes are durable.</summary>
    public void Complete()
    {
        undoActions.Clear();
        loggedStatements.Clear();
    }
}
