using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

/// <summary>
/// The live 2s poll merges fresh server data into the existing tree via NodeViewModel.ApplyUpdate.
/// These pin the property that an expanded compound survives the game re-serializing it (reordered
/// keys, an inserted/removed key) without collapsing — the reused VM instance keeps its expansion.
/// </summary>
public class NodeViewModelMergeTests
{
    private static NbtNode Int(int v) => new(NbtType.Int) { Scalar = v.ToString() };

    private static NbtNode Compound(params (string Name, NbtNode Value)[] kids)
    {
        var n = new NbtNode(NbtType.Compound) { Children = [] };
        foreach (var (name, value) in kids) n.Children!.Add(new(name, value));
        return n;
    }

    private static NodeViewModel Root(NbtNode node)
    {
        var vm = new NodeViewModel("player:x", "", node, null, "root");
        vm.IsExpanded = true;   // materialize the top-level children
        return vm;
    }

    [Fact]
    public void Reordered_keys_keep_the_same_child_instances_and_expansion()
    {
        var root = Root(Compound(
            ("a", Compound(("x", Int(1)))),
            ("b", Int(2)),
            ("c", Int(3))));
        var a = root.Children!.Single(c => c.Name == "a");
        a.IsExpanded = true;

        // server hands back the same keys in a different order (vanilla re-serialization)
        root.ApplyUpdate(Compound(
            ("c", Int(3)),
            ("a", Compound(("x", Int(1)))),
            ("b", Int(2))));

        Assert.Same(a, root.Children!.Single(c => c.Name == "a"));   // not rebuilt
        Assert.True(a.IsExpanded);                                    // still open
        Assert.Equal(new[] { "c", "a", "b" }, root.Children!.Select(c => c.Name));  // reordered to match
    }

    [Fact]
    public void Inserted_key_preserves_expanded_siblings()
    {
        var root = Root(Compound(
            ("a", Compound(("x", Int(1)))),
            ("b", Int(2))));
        var a = root.Children!.Single(c => c.Name == "a");
        a.IsExpanded = true;

        // an edit made the server add a "components" key before "b"
        root.ApplyUpdate(Compound(
            ("a", Compound(("x", Int(1)))),
            ("components", Int(9)),
            ("b", Int(2))));

        Assert.Same(a, root.Children!.Single(c => c.Name == "a"));
        Assert.True(a.IsExpanded);
        Assert.Equal(new[] { "a", "components", "b" }, root.Children!.Select(c => c.Name));
    }

    [Fact]
    public void Removed_key_is_dropped_and_the_rest_are_kept()
    {
        var root = Root(Compound(("a", Int(1)), ("b", Int(2)), ("c", Int(3))));
        var c = root.Children!.Single(n => n.Name == "c");

        root.ApplyUpdate(Compound(("a", Int(1)), ("c", Int(3))));   // "b" gone

        Assert.Equal(new[] { "a", "c" }, root.Children!.Select(n => n.Name));
        Assert.Same(c, root.Children!.Single(n => n.Name == "c"));
    }

    [Fact]
    public void Value_update_still_reaches_a_reused_child()
    {
        var root = Root(Compound(("a", Int(1)), ("b", Int(2))));
        var b = root.Children!.Single(n => n.Name == "b");

        root.ApplyUpdate(Compound(("b", Int(42)), ("a", Int(1))));   // reordered AND b changed

        Assert.Same(b, root.Children!.Single(n => n.Name == "b"));
        Assert.Equal("42", b.ValueText);
    }

    [Fact]
    public void Same_named_key_with_a_new_type_is_replaced()
    {
        var root = Root(Compound(("a", Int(1))));
        var oldA = root.Children!.Single(n => n.Name == "a");

        root.ApplyUpdate(Compound(("a", new NbtNode(NbtType.String) { Scalar = "hi" })));

        var newA = root.Children!.Single(n => n.Name == "a");
        Assert.NotSame(oldA, newA);                  // type change genuinely rebuilds the row
        Assert.Equal(NbtType.String, newA.Type);
    }
}
