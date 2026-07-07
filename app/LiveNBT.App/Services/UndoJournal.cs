namespace LiveNBT.App.Services;

/// <summary>
/// One undoable client-initiated mutation. Undo/Redo run the inverse/forward ops through the
/// normal request pipeline and return whether the server accepted them.
/// </summary>
public sealed record UndoEntry(string Description, Func<Task<bool>> Undo, Func<Task<bool>> Redo);

/// <summary>
/// Edit history for the live session. Snapshot-based: entries capture the pre-edit subtree from
/// the in-memory tree (fresh to within the 2 s poll) and restore it with a plain set/delete.
/// This is *edit* undo, not time travel — if the game changed a value in between, undo restores
/// the pre-edit snapshot over it. Cleared whenever the tree's identity changes (disconnect,
/// different root loaded) because entries reference paths of a specific server-side tree.
/// </summary>
public sealed class UndoJournal
{
    private const int Cap = 200;   // plenty for a session; bounds captured-subtree memory
    private readonly List<UndoEntry> _undo = [];
    private readonly List<UndoEntry> _redo = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Record(UndoEntry entry)
    {
        _undo.Add(entry);
        if (_undo.Count > Cap) _undo.RemoveAt(0);
        _redo.Clear();   // a new edit forks history; the redo branch is gone
    }

    /// <summary>Undo the newest entry. Returns its description, or null when there was nothing
    /// to undo or the server rejected the inverse (the entry is dropped either way — a failed
    /// inverse means the tree moved on and replaying it again would still fail).</summary>
    public async Task<string?> TryUndoAsync()
    {
        if (_undo.Count == 0) return null;
        UndoEntry entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        if (!await entry.Undo()) return null;
        _redo.Add(entry);
        return entry.Description;
    }

    public async Task<string?> TryRedoAsync()
    {
        if (_redo.Count == 0) return null;
        UndoEntry entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        if (!await entry.Redo()) return null;
        _undo.Add(entry);
        return entry.Description;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
