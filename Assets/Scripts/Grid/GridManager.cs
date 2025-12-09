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

    [Header("Wygląd")]
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
                quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // leży płasko na XZ
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
        PlaceTransferPoints();

        RTgrid = new List<Tile[,]>();
        for(int i=0; i<2000; i++) UpdateRTgrid(i, grid); //INICJALIZACJA PIERWSZYCH 2000 KROKÓW

        //UpdateRTgrid(5000); //TEST CZY TO PRZYSPIESZY
        RefreshAll(0);

        Debug.Log($"ilość gridów = {RTgrid.Count}");
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
        List<Tile> corridorTiles = new List<Tile>();

        for (int y = 0; y < length-2; y++)
        {
            for (int x = 2; x < width-2; x++)
            {
                int period = shelfWidth + corridorWidth;
                bool isShelfRow = (x % period) < shelfWidth;

                if (!isShelfRow)
                {
                    corridorTiles.Add(grid[x, y]);
                }
                else
                {
                    grid[x, y].flags &= ~(TileFlags.Shelf | TileFlags.Occupied | TileFlags.Blocked);
                }
            }
        }

        // Przejścia poprzeczne (odkorkuj na korytarze)
        for (int y = 0; y < length; y += 8)
            for (int x = 0; x < width; x++)
            {
                grid[x, y].flags &= ~(TileFlags.Shelf);
                corridorTiles.Remove(grid[x, y]);
            }

        for (int y = 1; y < length; y += 8)
            for (int x = 0; x < width; x++)
            {
                grid[x, y].flags &= ~(TileFlags.Shelf);
                corridorTiles.Remove(grid[x, y]);
            }

        // Ustal flagi półek (kolor nada Tile.UpdateColor)
        foreach (var t in corridorTiles)
        {
            t.flags |= TileFlags.Shelf;
            // losowe zajęcie półki
            if (Random.value < startShelfOccupation)
                t.flags |= (TileFlags.Occupied | TileFlags.Blocked);
        }
    }

    void PlaceSpawnPoints()
    {
        int spacing = width / 8;
        for (int i = 0; i < 8; i++)
        {
            int x = i * spacing + spacing / 2;
            spawnPoints.Add(new Vector2Int(x, 0));
            spawnPoints.Add(new Vector2Int(x, length - 1));
            grid[x, 0].flags |= (TileFlags.Spawn | TileFlags.Blocked); // kolor z flag
            grid[x, length - 1].flags |= (TileFlags.Spawn | TileFlags.Blocked);
        }

        //Debug.Log("Spawnpoints:");
        //foreach (var s in spawnPoints)
        //    Debug.Log($"({s.x}, {s.y})");
    }

    void PlaceTransferPoints()
    {
        int spacing = length / 8;
        for (int i = 0; i < 9; i++)
        {
            int y = i * spacing + spacing / 2;
            spawnPoints.Add(new Vector2Int(0, y));
            spawnPoints.Add(new Vector2Int(width - 1, y));
            grid[0, y].flags |= (TileFlags.TransferPoint /*| TileFlags.Blocked*/); // kolor z flag
            grid[width - 1, y].flags |= (TileFlags.TransferPoint);
        }
    }

    public void CheckSpawnpointsOccupation(int step)
    {
        var robots = gm.robotManager.AllRobots;
        foreach (var s in spawnPoints)
        {
            bool occupied = false;
            foreach (var r in robots)
            {
                if (r.GridPos.x == s.x && r.GridPos.y == s.y)
                {
                    occupied = true;
                    break;
                }
            }
            //chwilowy tile
            var tile = new Tile(s.x, s.y, width);
            tile.flags = RTgrid[step][s.x, s.y].flags;
            if (occupied)
                tile.flags |= TileFlags.Blocked;
            else
                tile.flags &= ~TileFlags.Blocked;
            UpdateRTgrid(step, null, tile);
        }
    }


    // Deep copy – tworzy NOWĄ tablicę i NOWE obiekty Tile (logiczne pola).
    // NIE przenosimy rendererów/meshy – te są tylko w głównym gridzie do rysowania.
    public Tile[,] CloneStep(Tile[,] src)
    {
        int w = src.GetLength(0), h = src.GetLength(1);
        var dst = new Tile[w, h];

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                var s = src[x, y];
                var t = new Tile(s.x, s.y, w); // liczy poprawnie index1D

                t.baseColor = s.baseColor; // <-- najpierw bazowy kolor
                t.flags = s.flags;     // <-- to wywoła UpdateColor() z użyciem baseColor
                t.cost = s.cost;
                t.heat = s.heat;
                dst[x, y] = t;
            }
        return dst;
    }



    public void UpdateRTgrid(int step, Tile[,] gridForStep = null, Tile spawnTile = null)
    {
        if (step < 0)
        {
            Debug.LogError("[GridManager] step < 0");
            return;
        }

        // Inicjalizacja listy kroków
        if (RTgrid == null) RTgrid = new List<Tile[,]>();

        // Jeżeli to pierwszy raz – dodaj krok 0 jako kopię aktualnego gridu sceny
        if (RTgrid.Count == 0)
        {
            // 'this.grid' – główna, renderowana siatka w scenie
            RTgrid.Add(CloneStep(this.grid));
        }

        // Przypadki:
        // 1) gridForStep == null  => chcemy skopiować poprzedni krok
        // 2) gridForStep != null  => chcemy użyć przekazanej siatki (ale dodać JEJ kopię)

        if (gridForStep == null)
        {
            if (step > RTgrid.Count)
            {
                UpdateRTgrid(step - 1);
                Debug.LogError($"[GridManager] Nieciągłość kroków (brak kroków pośrednich). Wywałany step: {step}");
            }

            if (step == RTgrid.Count)
            {
                // dodaj jako nowy krok – KOPIĘ poprzedniego
                RTgrid.Add(CloneStep(RTgrid[step - 1]));
            }
            else if (step < RTgrid.Count)
            {
                //nadpisanie przyszłych spawnpointów zmianą flagi
                if (spawnTile != null)
                {
                    for (int i = step; i < RTgrid.Count; i++) 
                    {
                        RTgrid[i][spawnTile.x, spawnTile.y].flags = spawnTile.flags;
                    }
                }
                //else RTgrid[step] = CloneStep(RTgrid[step]);
                // „nic się nie zmieniło” – ale jeśli chcesz, możesz nadpisać kopią poprzedniego
                // RTgrid[step] = CloneStep(RTgrid[step]); // zwykle NO-OP
                return;
            }
            
        }
        else
        {
            if (step == RTgrid.Count)
            {
                // nowy krok – dodaj KOPIĘ przekazanego gridu
                RTgrid.Add(CloneStep(gridForStep));
            }
            else if (step < RTgrid.Count)
            {
                // nadpisz istniejący krok – KOPIĄ przekazanego gridu
                RTgrid[step] = CloneStep(gridForStep);
            }
            else
            {
                Debug.LogError("[GridManager] Nieciągłość kroków (brak kroków pośrednich).");
            }
            CheckSpawnpointsOccupation(step);
        }
    }


}
