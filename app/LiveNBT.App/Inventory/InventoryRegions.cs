namespace LiveNBT.App.Inventory;

/// <summary>The fixed slot-number layout of the player inventory.</summary>
public static class InventoryRegions
{
    public static readonly int[] Hotbar = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    public static readonly int[] Main = Enumerable.Range(9, 27).ToArray();   // 9..35
    public static readonly int[] Armor = [39, 38, 37, 36];                   // head, chest, legs, feet
    public static readonly int[] Offhand = [40];

    public static readonly int[] SlotOrder = Hotbar.Concat(Main).Concat(Armor).Concat(Offhand).ToArray();
}
