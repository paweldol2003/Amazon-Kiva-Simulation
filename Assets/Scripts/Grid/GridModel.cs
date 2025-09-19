using UnityEngine;

[System.Serializable]
public struct GridConfig { public int width, length; public float cellSize; public Vector2 origin; }

public class GridModel : MonoBehaviour
{
    public GridConfig cfg = new GridConfig { width = 10, length = 10, cellSize = 1f, origin = Vector2.zero };
    public Tile[,] tiles;

    void Awake() => Build();

    [ContextMenu("Rebuild")]
    public void Build()
    {
        tiles = new Tile[cfg.width, cfg.length];
        for (int y = 0; y < cfg.length; y++)
            for (int x = 0; x < cfg.width; x++)
                tiles[x, y] = new Tile(x, y, cfg.width);
    }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < cfg.width && y < cfg.length;

    public bool WorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - cfg.origin.x) / cfg.cellSize);
        y = Mathf.FloorToInt((world.y - cfg.origin.y) / cfg.cellSize);
        return InBounds(x, y);
    }

    public Vector3 CellCenter(int x, int y)
        => new Vector3(cfg.origin.x + (x + 0.5f) * cfg.cellSize, cfg.origin.y + (y + 0.5f) * cfg.cellSize, 0);
}
