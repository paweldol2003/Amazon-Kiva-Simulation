using UnityEngine;

public class GridManager : MonoBehaviour
{
    private GameManager gm;

    [Header("Grid")]
    public int width = 10;
    public int length = 10;
    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;

    [Header("Wygl¹d")]
    public Material tileMaterial; // np. URP/Unlit lub Standard
    //public Color defaultColor = new Color(0.18f, 0.18f, 0.18f, 1f);

    public Tile[,] grid;
    private MeshRenderer[,] renderers;

    public void Init(GameManager gm) => this.gm = gm;

    void Start()
    {
        grid = new Tile[width, length];
        renderers = new MeshRenderer[width, length];

        for (int y = 0; y < length; y++)
            for (int x = 0; x < width; x++)
            {
                grid[x, y] = new Tile(x, y, width);
                //tiles[x, y].color = defaultColor;

                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"Tile_{x}_{y}";
                quad.transform.SetParent(transform, false);
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // le¿y p³asko na XZ
                quad.transform.localScale = new Vector3(cellSize, cellSize, 1f);
                quad.transform.position = new Vector3(
                    origin.x + (x + 0.5f) * cellSize,
                    0f,
                    origin.y + (y + 0.5f) * cellSize
                );
                
                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = new Material(tileMaterial ?? new Material(Shader.Find("Unlit/Color")));
                mr.sharedMaterial.color = grid[x, y].color;
                renderers[x, y] = mr;
            }
    }


    public void SetTileColor(int x, int y, Color color)
    {
        if (!InBounds(x, y)) return;
        grid[x, y].color = color;
        renderers[x, y].sharedMaterial.color = color;
    }

    public void RefreshCell(int x, int y)
    {
        if (!InBounds(x, y)) return;
        renderers[x, y].sharedMaterial.color = grid[x, y].color;
    }

    public void RefreshAll()
    {
        for (int y = 0; y < length; y++)
            for (int x = 0; x < width; x++)
                renderers[x, y].sharedMaterial.color = grid[x, y].color;
    }

    // --- Pomocnicze --------------------------------------------------

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < length;

    public bool WorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - origin.x) / cellSize);
        y = Mathf.FloorToInt((world.z - origin.y) / cellSize);
        return InBounds(x, y);
    }

    public Vector3 CellCenter(int x, int y)
    {
        return new Vector3(
            origin.x + (x + 0.5f) * cellSize,
            0f,
            origin.y + (y + 0.5f) * cellSize
        );
    }
}
