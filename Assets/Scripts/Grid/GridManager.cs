using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class GridManager : MonoBehaviour
{
    private GameManager gm;

    [Header("Grid")]
    public int width = 106;
    public int length = 46;
    [Range(0f, 1f)] public float startShelfOccupation = 0.3f;

    [Header("Kolory kafli")]
    public TileColors tileColors = new TileColors();


    private List<Vector2Int> spawnPoints = new List<Vector2Int>();


    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;

    [Header("Wygl¹d")]
    public Material tileMaterial; // np. URP/Unlit lub Standard

    private Tile[,] grid;
    public List<Tile[,]> RTgrid; //Real time grid

    private MeshRenderer[,] renderers;

    public void Init(GameManager gm) => this.gm = gm;

    void Awake()
    {
        grid = new Tile[width, length];
        renderers = new MeshRenderer[width, length];
        Tile.Palette = tileColors;


        for (int y = 0; y < length; y++)
            for (int x = 0; x < width; x++)
            {
                grid[x, y] = new Tile(x, y, width);

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

        SetupShelvesAndCorridors();
        PlaceSpawnPoints();

        RTgrid = new List<Tile[,]>();
        UpdateRTgrid(0, grid);
        RefreshAll(0);

        Debug.Log($"iloœæ gridów = {RTgrid.Count}");
    }


    public void RefreshAll(int Step)
    {
        for (int y = 0; y < length; y++)
            for (int x = 0; x < width; x++)
                renderers[x, y].sharedMaterial.color = RTgrid[Step][x,y].color;
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


    void SetupShelvesAndCorridors()
    {
        int shelfWidth = 2;
        int corridorWidth = 2;
        List<Tile> shelfTiles = new List<Tile>();

        for (int y = 0; y < length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isShelfRow =
                    ((x / (shelfWidth + corridorWidth)) * (shelfWidth + corridorWidth) <= x) &&
                    (x % (shelfWidth + corridorWidth) < shelfWidth);

                if (isShelfRow)
                {
                    shelfTiles.Add(grid[x, y]);
                }
                else
                {
                    grid[x, y].flags &= ~(TileFlags.Shelf | TileFlags.Occupied | TileFlags.Blocked);
                }
            }
        }

        // Przejœcia poprzeczne (odkorkuj na korytarze)
        for (int y = 0; y < length; y += 7)
            for (int x = 0; x < width; x++)
            {
                grid[x, y].flags &= ~(TileFlags.Shelf | TileFlags.Occupied | TileFlags.Blocked);
                shelfTiles.Remove(grid[x, y]);
            }

        for (int y = 1; y < length; y += 7)
            for (int x = 0; x < width; x++)
            {
                grid[x, y].flags &= ~(TileFlags.Shelf | TileFlags.Occupied | TileFlags.Blocked);
                shelfTiles.Remove(grid[x, y]);
            }

        // Ustal flagi pó³ek (kolor nada Tile.UpdateColor)
        foreach (var t in shelfTiles)
        {
            t.flags |= TileFlags.Shelf;
            // losowe zajêcie pó³ki
            if (Random.value < startShelfOccupation)
                t.flags |= TileFlags.Occupied;
            else
                t.flags &= ~TileFlags.Occupied;
            // UWAGA: nie ruszamy rendererów – kolor ustawi siê z flag przy PushColors
        }
    }


    void PlaceSpawnPoints()
    {
        int spacing = width / 8;
        for (int i = 0; i < 8; i++)
        {
            int x = i * spacing + spacing / 2;
            spawnPoints.Add(new Vector2Int(x, 0));
            grid[x, 0].flags |= TileFlags.Spawn; // kolor z flag
        }

        Debug.Log("Spawnpoints:");
        foreach (var s in spawnPoints)
            Debug.Log($"({s.x}, {s.y})");
    }


    // Deep copy – tworzy NOW¥ tablicê i NOWE obiekty Tile (logiczne pola).
    // NIE przenosimy rendererów/meshy – te s¹ tylko w g³ównym gridzie do rysowania.
    private Tile[,] CloneStep(Tile[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new Tile[w, h];

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                var s = src[x, y];
                var t = new Tile(s.x, s.y, w); // liczy poprawnie index1D

                t.baseColor = s.baseColor; // <-- najpierw bazowy kolor
                t.flags = s.flags;     // <-- to wywo³a UpdateColor() z u¿yciem baseColor
                t.cost = s.cost;
                t.heat = s.heat;
                dst[x, y] = t;
            }
        return dst;
    }



    public void UpdateRTgrid(int step, Tile[,] gridForStep = null)
    {
        if (step < 0)
        {
            Debug.LogError("[GridManager] step < 0");
            return;
        }

        // Inicjalizacja listy kroków
        if (RTgrid == null) RTgrid = new List<Tile[,]>();

        // Je¿eli to pierwszy raz – dodaj krok 0 jako kopiê aktualnego gridu sceny
        if (RTgrid.Count == 0)
        {
            // 'this.grid' – g³ówna, renderowana siatka w scenie
            RTgrid.Add(CloneStep(this.grid));
        }

        // Przypadki:
        // 1) gridForStep == null  => chcemy skopiowaæ poprzedni krok
        // 2) gridForStep != null  => chcemy u¿yæ przekazanej siatki (ale dodaæ JEJ kopiê)

        if (gridForStep == null)
        {
            if (step == RTgrid.Count)
            {
                // dodaj jako nowy krok – KOPIÊ poprzedniego
                RTgrid.Add(CloneStep(RTgrid[step - 1]));
            }
            else if (step < RTgrid.Count)
            {
                // „nic siê nie zmieni³o” – ale jeœli chcesz, mo¿esz nadpisaæ kopi¹ poprzedniego
                // RTgrid[step] = CloneStep(RTgrid[step]); // zwykle NO-OP
                return;
            }
            else
            {
                Debug.LogError("[GridManager] Nieci¹g³oœæ kroków (brak kroków poœrednich).");
            }
        }
        else
        {
            if (step == RTgrid.Count)
            {
                // nowy krok – dodaj KOPIÊ przekazanego gridu
                RTgrid.Add(CloneStep(gridForStep));
            }
            else if (step < RTgrid.Count)
            {
                // nadpisz istniej¹cy krok – KOPI¥ przekazanego gridu
                RTgrid[step] = CloneStep(gridForStep);
            }
            else
            {
                Debug.LogError("[GridManager] Nieci¹g³oœæ kroków (brak kroków poœrednich).");
            }
        }
    }


}
