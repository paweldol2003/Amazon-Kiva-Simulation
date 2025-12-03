using System;
using UnityEngine;

[Flags]
public enum TileFlags : byte
{
    None = 0,
    Blocked = 1 << 0,
    //Reserved = 1 << 1,
    Spawn = 1 << 2,
    Goal = 1 << 3,
    Shelf = 1 << 4,
    Occupied = 1 << 5,
    TransferPoint = 1 << 6,
    AlgPath = 1 << 7,
    BestAlgPath = 1 << 1,

}

[Serializable]
public class Tile
{
    public int x, y, index1D;
    public int cost = 1;
    public float heat = 0f;

    public Color32 baseColor;
    public Color32 color;

    private TileFlags _flags = TileFlags.None;
    public TileFlags flags
    {
        get => _flags;
        set { _flags = value; UpdateColor(); }
    }

    public bool Walkable => (_flags & TileFlags.Blocked) == 0;

    // kolory s¹ globalne – przypisuje je GridManager
    public static TileColors Palette;

    public Tile(int x, int y, int width)
    {
        this.x = x; this.y = y;
        index1D = y * width + x;
        baseColor = Palette != null ? Palette.walkable : new Color32(245, 245, 245, 255);
        color = baseColor;
    }

    public void Add(TileFlags f) => flags |= f;
    public void Remove(TileFlags f) => flags &= ~f;

    public void UpdateColor()
    {
        var C = Palette ?? new TileColors(); // awaryjnie gdyby null
        if ((_flags & (TileFlags.Shelf | TileFlags.Occupied)) == (TileFlags.Shelf | TileFlags.Occupied)) { color = C.occupiedShelf; return; }
        if ((_flags & TileFlags.Shelf) != 0) { color = C.shelf; return; }
        if ((_flags & TileFlags.Blocked) != 0) { color = C.blocked; return; }
        if ((_flags & TileFlags.Spawn) != 0) { color = C.spawn; return; }
        if ((_flags & TileFlags.Spawn | TileFlags.Blocked) == 0) { color = C.blocked; return; }
        if ((_flags & TileFlags.Goal) != 0) { color = C.goal; return; }
        //if ((_flags & TileFlags.Reserved) != 0) { color = C.reserved; return; }
        if ((_flags & TileFlags.TransferPoint) != 0) { color = C.transferpoint; return; }
        if ((_flags & TileFlags.TransferPoint | TileFlags.Blocked) == 0) { color = C.blocked; return; }
        if ((_flags & TileFlags.BestAlgPath) != 0) { color = C.bestAlgPath; return; }
        if ((_flags & TileFlags.AlgPath) != 0) { color = C.algPath; return; }

        color = baseColor;
    }
}
[Serializable]
public class TileColors
{
    [Header("Podstawowe kolory")]
    public Color32 walkable = new Color32(245, 245, 245, 255);
    public Color32 blocked = new Color32(0, 0, 0, 255);

    [Header("Rega³y")]
    public Color32 shelf = new Color32(100, 100, 100, 255);
    public Color32 occupiedShelf = new Color32(150, 60, 0, 255);

    [Header("Specjalne")]
    public Color32 reserved = new Color32(0, 200, 80, 255);
    public Color32 spawn = new Color32(40, 120, 255, 255);
    public Color32 goal = new Color32(255, 210, 0, 255);
    public Color32 transferpoint = new Color32(127, 0, 255, 100);
    public Color32 algPath = new Color32(255, 0, 0, 255);
    public Color32 bestAlgPath = new Color32(255, 215, 0, 255);
}
