using System.Globalization;
using LiveNBT.App.Services;
using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;
using Xunit;

namespace LiveNBT.Tests;

public class InventoryViewModelTests
{
    private sealed class FakeSession : IServerSession
    {
        public readonly List<(string Op, string? Root, string? Path, NbtNode? Value)> Sent = [];
        public Func<string, ServerMessage>? Responder;
        public Task<ServerMessage> RequestAsync(string op, string? root = null, string? path = null, NbtNode? value = null)
        {
            Sent.Add((op, root, path, value));
            return Task.FromResult(Responder?.Invoke(op) ?? new ServerMessage { Ok = true });
        }
    }

    private static NbtNode Int(int i) => new(NbtType.Int) { Scalar = i.ToString(CultureInfo.InvariantCulture) };
    private static NbtNode Str(string s) => new(NbtType.String) { Scalar = s };

    private static NbtNode Snapshot()
    {
        var children = new List<KeyValuePair<string, NbtNode>>();
        foreach (int n in Enumerable.Range(0, 41))
        {
            NbtNode item = n == 0
                ? new NbtNode(NbtType.Compound) { Children = [new("id", Str("minecraft:stone")), new("count", Int(1))] }
                : new NbtNode(NbtType.Compound) { Children = [] };
            children.Add(new(n.ToString(), item));
        }
        return new NbtNode(NbtType.Compound) { Children = children };
    }

    private static InventoryViewModel NewVm(FakeSession s) =>
        new(s, "inventory:Bob", ["minecraft:stone", "minecraft:diamond_sword"], ["minecraft:sharpness"]);

    [Fact]
    public void HasFortyOneSlotsGroupedByRegion()
    {
        var vm = NewVm(new FakeSession());
        Assert.Equal(41, vm.Slots.Count);
        Assert.Equal(9, vm.Hotbar.Count);
        Assert.Equal(27, vm.Main.Count);
        Assert.Equal(4, vm.Armor.Count);
        Assert.Single(vm.Offhand);
    }

    [Fact]
    public async Task LoadParsesSnapshotIntoSlots()
    {
        var s = new FakeSession { Responder = _ => new ServerMessage { Ok = true, Value = Snapshot() } };
        var vm = NewVm(s);
        await vm.LoadAsync();
        Assert.Equal("minecraft:stone", vm.Slots.First(x => x.SlotNumber == 0).ItemId);
        Assert.True(vm.Slots.First(x => x.SlotNumber == 5).IsEmpty);
        Assert.Equal(("get", "inventory:Bob", ""), (s.Sent[0].Op, s.Sent[0].Root, s.Sent[0].Path));
    }

    [Fact]
    public async Task ApplyOccupiedSlotSendsSet()
    {
        var s = new FakeSession();
        var vm = NewVm(s);
        var slot = vm.Slots.First(x => x.SlotNumber == 0);
        slot.ItemId = "minecraft:diamond_sword";
        await vm.ApplyAsync(slot);

        var sent = s.Sent.Single();
        Assert.Equal("set", sent.Op);
        Assert.Equal("slot.0", sent.Path);
        Assert.Equal("minecraft:diamond_sword", sent.Value!.Child("id")!.Scalar);
    }

    [Fact]
    public async Task ApplyEmptySlotSendsDelete()
    {
        var s = new FakeSession();
        var vm = NewVm(s);
        await vm.ApplyAsync(vm.Slots.First(x => x.SlotNumber == 0));   // empty by default
        Assert.Equal("delete", s.Sent.Single().Op);
        Assert.Equal("slot.0", s.Sent.Single().Path);
    }

    [Fact]
    public void ItemQueryFiltersList()
    {
        var vm = NewVm(new FakeSession());
        vm.SetItemQuery("sword");
        Assert.Equal(["minecraft:diamond_sword"], vm.FilteredItemIds);
    }
}
