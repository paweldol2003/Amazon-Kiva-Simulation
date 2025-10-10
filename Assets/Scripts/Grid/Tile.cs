using System;
using UnityEngine;

[Flags]
public enum TileFlags : byte { None = 0, Blocked = 1 << 0, Reserved = 1 << 1, Spawn = 1 << 2, Goal = 1 << 3 }

[Serializable]
public class Tile
{
    public int x, y;              // indeks na siatce
    public int index1D;           // y*width + x
    public TileFlags flags;       // bitowe flagi
    public int cost = 1;          // koszt ruchu
    public float heat = 0f;       // ciep³o
    public Color32 color;         // kolor wizualny

    public bool Walkable => (flags & TileFlags.Blocked) == 0;

    public Tile(int x, int y, int width)
    {
        this.x = x; this.y = y;
        index1D = y * width + x;
        color = new Color32(255, 255, 255, 255);
    }
}
