using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class NodeViewModelTests
{
    private static NbtNode SampleRoot()
    {
        return new NbtNode(NbtType.Compound)
        {
            Children =
            [
                new("abilities", new NbtNode(NbtType.Compound)
                {
                    Children = [new("mayfly", new NbtNode(NbtType.Byte) { Scalar = "0" })],
                }),
                new("Pos", new NbtNode(NbtType.List)
                {
                    Items = [new NbtNode(NbtType.Double) { Scalar = "1.5" }, new NbtNode(NbtType.Double) { Scalar = "64" }],
                }),
            ],
        };
    }

    [Fact]
    public void BuildsPathsAndLazyChildren()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        Assert.Equal("", root.Path);
        root.IsExpanded = true;
        var abilities = root.Children![0];
        Assert.Equal("abilities", abilities.Path);
        abilities.IsExpanded = true;
        Assert.Equal("abilities.mayfly", abilities.Children![0].Path);
        var pos = root.Children![1];
        pos.IsExpanded = true;
        Assert.Equal("Pos[0]", pos.Children![0].Path);
        Assert.Equal("Pos[1]", pos.Children![1].Path);
    }

    [Fact]
    public void UnexpandedContainerShowsPlaceholderChild()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        // WPF needs a non-empty Children collection to draw an expander arrow
        Assert.Single(root.Children!);
        Assert.Equal("…", root.Children![0].Name);
        root.IsExpanded = true;
        Assert.Equal(2, root.Children!.Count);
    }

    [Fact]
    public void ScalarLeavesAreEditable_ContainersAreNot()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        root.Children![0].IsExpanded = true;
        Assert.True(root.Children![0].Children![0].CanEdit);   // mayfly (byte)
        Assert.False(root.Children![0].CanEdit);               // abilities (compound)
    }

    [Fact]
    public void ApplyUpdateChangesScalarAndFlagsConflictWhileEditing()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        root.Children![0].IsExpanded = true;
        NodeViewModel mayfly = root.Children![0].Children![0];
        mayfly.ApplyUpdate(new NbtNode(NbtType.Byte) { Scalar = "1" });
        Assert.Equal("1", mayfly.ValueText);
        Assert.False(mayfly.ConflictHint);

        mayfly.IsEditing = true;
        mayfly.ApplyUpdate(new NbtNode(NbtType.Byte) { Scalar = "0" });
        Assert.True(mayfly.ConflictHint);   // changed under the user's cursor
        mayfly.IsEditing = false;
        Assert.False(mayfly.ConflictHint);  // cleared once editing ends
    }

    [Fact]
    public void ContainerSummaryShowsCount()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        Assert.Equal("2 entries", root.Children![1].ValueText); // Pos list
    }

    [Fact]
    public void FindByPathLocatesMaterializedDescendants()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        root.Children![0].IsExpanded = true;
        Assert.NotNull(root.FindByPath("abilities.mayfly"));
        Assert.Null(root.FindByPath("abilities.nope"));
        Assert.Same(root, root.FindByPath(""));
    }

    [Fact]
    public void ApplyUpdatePreservesExpansionOfDescendants()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        var abilities = root.Children![0];
        abilities.IsExpanded = true;
        var abilitiesVmBefore = root.Children![0];

        // fresh snapshot of the same shape, mayfly flipped to 1
        var fresh = SampleRoot();
        fresh.Child("abilities")!.Child("mayfly")!.Scalar = "1";
        root.ApplyUpdate(fresh);

        Assert.Same(abilitiesVmBefore, root.Children![0]);     // VM instance survives
        Assert.True(root.Children![0].IsExpanded);             // expansion survives
        Assert.Equal("1", root.Children![0].Children![0].ValueText); // value propagated
    }

    [Fact]
    public void ApplyUpdateAddsAndRemovesListEntries()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        var pos = root.Children![1];
        pos.IsExpanded = true;
        Assert.Equal(2, pos.Children!.Count);

        var fresh = new NbtNode(NbtType.List)
        {
            Items = [new NbtNode(NbtType.Double) { Scalar = "9" }],
        };
        pos.ApplyUpdate(fresh);
        Assert.Single(pos.Children!);
        Assert.Equal("9", pos.Children![0].ValueText);
    }

    [Fact]
    public void EmptyContainerGainingEntriesGetsAnExpander()
    {
        var empty = new NbtNode(NbtType.Compound) { Children = [] };
        var vm = new NodeViewModel("player:Bob", "tags", empty, null, "tags");
        Assert.Empty(vm.Children!);
        var fresh = new NbtNode(NbtType.Compound)
        {
            Children = [new("a", new NbtNode(NbtType.Int) { Scalar = "1" })],
        };
        vm.ApplyUpdate(fresh);
        Assert.Single(vm.Children!);   // placeholder appeared
        vm.IsExpanded = true;
        Assert.Equal("a", vm.Children![0].Name);
    }

    [Fact]
    public void ExpandingANodeFiresOnExpandWithThatNode()
    {
        var fired = new List<string>();
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null, "", n => fired.Add(n.Path));
        root.IsExpanded = true;                 // root expand fires
        root.Children![0].IsExpanded = true;    // "abilities" expand fires
        Assert.Contains("", fired);
        Assert.Contains("abilities", fired);
    }

    [Fact]
    public void AutoPollDoesNotFalselyFlagAnUneditedOrUnchangedValue()
    {
        var root = new NodeViewModel("player:Bob", "", SampleRoot(), null);
        root.IsExpanded = true;
        root.Children![0].IsExpanded = true;
        var mayfly = root.Children![0].Children![0];

        // not editing: no conflict regardless of change
        mayfly.ApplyUpdate(new NbtNode(NbtType.Byte) { Scalar = "1" });
        Assert.False(mayfly.ConflictHint);

        // editing, value unchanged (poll re-applies same value): still no conflict
        mayfly.IsEditing = true;
        mayfly.ApplyUpdate(new NbtNode(NbtType.Byte) { Scalar = "1" });
        Assert.False(mayfly.ConflictHint);

        // editing, value actually changed on server: conflict
        mayfly.ApplyUpdate(new NbtNode(NbtType.Byte) { Scalar = "0" });
        Assert.True(mayfly.ConflictHint);
    }
}
