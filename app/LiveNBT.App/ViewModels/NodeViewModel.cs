using System.Collections.ObjectModel;
using LiveNBT.Protocol;

namespace LiveNBT.App.ViewModels;

/// <summary>One row in the NBT tree. Children materialize on first expand.</summary>
public sealed class NodeViewModel : ViewModelBase
{
    private NbtNode _node;
    private bool _isExpanded;
    private bool _isEditing;
    private bool _isSelected;
    private bool _conflictHint;
    private string _flash = "";   // "" | "ok" | "error"
    private bool _materialized;
    private readonly Action<NodeViewModel>? _onExpand;

    public NodeViewModel(string root, string path, NbtNode node, NodeViewModel? parent, string name = "", Action<NodeViewModel>? onExpand = null)
    {
        _onExpand = onExpand;
        Root = root;
        Path = path;
        Name = name;
        Parent = parent;
        _node = node;
        Children = NbtTypes.IsScalar(node.Type) ? null : new ObservableCollection<NodeViewModel>();
        // WPF only renders an expander arrow when Children is non-empty, so a non-empty
        // container gets a placeholder child until first expand materializes the real ones.
        if (Children is not null && ContainerCount(node) > 0)
            Children.Add(new NodeViewModel(root, path + "\0placeholder", new NbtNode(NbtType.String), this, "…"));
    }

    private static int ContainerCount(NbtNode node) => node.Items?.Count ?? node.Children?.Count ?? 0;

    public string Root { get; }
    public string Path { get; }
    public string Name { get; }
    public NodeViewModel? Parent { get; }
    public NbtType Type => _node.Type;
    public NbtNode Node => _node;
    public ObservableCollection<NodeViewModel>? Children { get; }
    public bool CanEdit => NbtTypes.IsScalar(_node.Type);

    // ----- capability flags for the context menu / hotkeys (protocol rules baked in:
    // world: roots are a virtual tree with no add/delete; inventory: has no add) -----
    private bool IsPlaceholder => Name == "…";
    private bool IsPlayerRoot => Root.StartsWith("player:", StringComparison.Ordinal);
    private bool IsInventoryRoot => Root.StartsWith("inventory:", StringComparison.Ordinal);
    private bool IsWholeSlot => IsInventoryRoot && Parent is { Path.Length: 0 };

    /// <summary>Structural container that accepts new children (add/paste/quick-add).</summary>
    public bool CanAddChild => !IsPlaceholder && IsPlayerRoot && _node.Type is NbtType.Compound or NbtType.List;
    public bool CanDelete => !IsPlaceholder && Path.Length > 0 && (IsPlayerRoot || IsWholeSlot);
    public bool CanDuplicate => !IsPlaceholder && IsPlayerRoot &&
        Parent is { } p && p.Type is NbtType.Compound or NbtType.List;
    public bool CanRename => !IsPlaceholder && IsPlayerRoot && Parent?.Type == NbtType.Compound;
    /// <summary>List/array element that can be reordered (rewrites the parent container via set).</summary>
    public bool CanMove => !IsPlaceholder && IsPlayerRoot &&
        Parent?.Type is NbtType.List or NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray;
    /// <summary>Whole-subtree SNBT editing: player paths anywhere; elsewhere only where set works
    /// (scalars on world roots, whole slots on inventory roots).</summary>
    public bool CanEditSnbt => !IsPlaceholder && Path.Length > 0 && (IsPlayerRoot || CanEdit || IsWholeSlot);

    public int IndexInParent => Parent?.Children?.IndexOf(this) ?? -1;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Hover detail: full text for long/multiline strings, an element preview for arrays.</summary>
    public string? ToolTipText
    {
        get
        {
            if (_node.Type == NbtType.String && _node.Scalar is { } s && (s.Length > 100 || s.Contains('\n')))
                return s.Length > 2000 ? s[..2000] + " …" : s;
            if (_node.Type is NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray && _node.Items is { Count: > 0 } items)
            {
                var preview = string.Join(", ", items.Take(24).Select(i => i.Scalar));
                return items.Count > 24 ? $"[{preview}, … {items.Count} total]" : $"[{preview}]";
            }
            return null;
        }
    }

    public string TypeGlyph => _node.Type switch
    {
        NbtType.Byte => "B", NbtType.Short => "S", NbtType.Int => "I", NbtType.Long => "L",
        NbtType.Float => "F", NbtType.Double => "D", NbtType.String => "T",
        NbtType.List => "[]", NbtType.Compound => "{}",
        NbtType.ByteArray => "[B]", NbtType.IntArray => "[I]", NbtType.LongArray => "[L]",
        _ => "?",
    };

    public string ValueText => NbtTypes.IsScalar(_node.Type)
        ? _node.Scalar ?? ""
        : $"{ContainerCount(_node)} entries";

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (Set(ref _isExpanded, value) && value)
            {
                Materialize();
                _onExpand?.Invoke(this);
            }
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (Set(ref _isEditing, value) && !value) ConflictHint = false;
        }
    }

    public bool ConflictHint
    {
        get => _conflictHint;
        private set => Set(ref _conflictHint, value);
    }

    public string Flash
    {
        get => _flash;
        set
        {
            if (value.Length > 0 && _flash == value) { _flash = ""; Raise(); } // force re-fire for repeated flashes
            Set(ref _flash, value);
        }
    }

    /// <summary>Expand without firing the per-node refresh callback — used by bulk expand-all,
    /// where one request per node would be a storm (the 2 s whole-root poll keeps data fresh).</summary>
    public void ExpandSilently()
    {
        if (Children is null || _isExpanded) return;
        _isExpanded = true;
        Materialize();
        Raise(nameof(IsExpanded));
    }

    private void Materialize()
    {
        if (_materialized || Children is null) return;
        _materialized = true;
        Children.Clear();
        if (_node.Children is not null)
        {
            foreach (var (name, child) in _node.Children)
                Children.Add(new NodeViewModel(Root, ChildPath(name), child, this, name, _onExpand));
        }
        else if (_node.Items is not null)
        {
            for (int i = 0; i < _node.Items.Count; i++)
                Children.Add(new NodeViewModel(Root, $"{Path}[{i}]", _node.Items[i], this, $"[{i}]", _onExpand));
        }
    }

    private string ChildPath(string name) => Path.Length == 0 ? name : $"{Path}.{name}";

    /// <summary>Incoming live value for exactly this path. Merges children in place so
    /// descendant expansion/editing state survives 5 Hz watch updates.</summary>
    public void ApplyUpdate(NbtNode fresh)
    {
        bool valueChanged = _node.Scalar != fresh.Scalar;
        _node = fresh;
        if (IsEditing && valueChanged) ConflictHint = true;
        Raise(nameof(ValueText));
        Raise(nameof(TypeGlyph));
        Raise(nameof(CanEdit));
        Raise(nameof(ToolTipText));
        Raise(nameof(CanAddChild));
        Raise(nameof(CanEditSnbt));
        if (Children is null) return;
        if (!_materialized)
        {
            // not expanded yet: just keep the expander arrow truthful
            if (Children.Count == 0 && ContainerCount(fresh) > 0)
                Children.Add(Placeholder());
            else if (Children.Count == 1 && Children[0].Name == "…" && ContainerCount(fresh) == 0)
                Children.Clear();
            return;
        }
        MergeChildren();
    }

    private NodeViewModel Placeholder() =>
        new(Root, Path + "\0placeholder", new NbtNode(NbtType.String), this, "…");

    /// <summary>Reconcile materialized child VMs against _node: update survivors in place
    /// (recursing), append new entries, trim removed ones. Compounds match by name+position,
    /// lists/arrays by index.</summary>
    private void MergeChildren()
    {
        if (Children is null) return;
        if (_node.Children is not null)
        {
            for (int i = 0; i < _node.Children.Count; i++)
            {
                var (name, childNode) = _node.Children[i];
                if (i < Children.Count && Children[i].Name == name && Children[i].Type == childNode.Type)
                {
                    Children[i].ApplyUpdate(childNode);
                }
                else
                {
                    var replacement = new NodeViewModel(Root, ChildPath(name), childNode, this, name, _onExpand);
                    if (i < Children.Count) Children[i] = replacement;
                    else Children.Add(replacement);
                }
            }
            while (Children.Count > _node.Children.Count) Children.RemoveAt(Children.Count - 1);
        }
        else if (_node.Items is not null)
        {
            for (int i = 0; i < _node.Items.Count; i++)
            {
                if (i < Children.Count && Children[i].Type == _node.Items[i].Type)
                {
                    Children[i].ApplyUpdate(_node.Items[i]);
                }
                else
                {
                    var replacement = new NodeViewModel(Root, $"{Path}[{i}]", _node.Items[i], this, $"[{i}]", _onExpand);
                    if (i < Children.Count) Children[i] = replacement;
                    else Children.Add(replacement);
                }
            }
            while (Children.Count > _node.Items.Count) Children.RemoveAt(Children.Count - 1);
        }
    }

    /// <summary>Walk this subtree for a descendant by its full path; null if not materialized/found.</summary>
    public NodeViewModel? FindByPath(string path)
    {
        if (path == Path) return this;
        if (Children is null) return null;
        foreach (var child in Children)
        {
            if (path == child.Path || path.StartsWith(child.Path + ".", StringComparison.Ordinal)
                || path.StartsWith(child.Path + "[", StringComparison.Ordinal))
            {
                var hit = child.FindByPath(path);
                if (hit is not null) return hit;
            }
        }
        return null;
    }
}
