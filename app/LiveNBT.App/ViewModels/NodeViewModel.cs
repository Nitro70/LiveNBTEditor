using System.Collections.ObjectModel;
using LiveNBT.Protocol;

namespace LiveNBT.App.ViewModels;

/// <summary>One row in the NBT tree. Children materialize on first expand.</summary>
public sealed class NodeViewModel : ViewModelBase
{
    private NbtNode _node;
    private bool _isExpanded;
    private bool _isEditing;
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
    public NbtType Type => _node.Type;
    public NbtNode Node => _node;
    public ObservableCollection<NodeViewModel>? Children { get; }
    public bool CanEdit => NbtTypes.IsScalar(_node.Type);

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
