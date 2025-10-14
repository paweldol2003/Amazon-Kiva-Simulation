using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PathManager : MonoBehaviour
{

    private GameManager gm;
    private Tile[,] grid;
    public enum PathfindingAlgorithm { AStar, ACO, ABC }
    [Header("Pathfinding")]
    public PathfindingAlgorithm algorithm = PathfindingAlgorithm.ACO;
    public void Init(GameManager gm)
    {
        this.gm = gm;
    }
    void Start()
    {
        this.grid = gm.gridManager.grid; // teraz gm ju¿ istnieje
    }

    // Update is called once per frame
    void Update()
    {

    }

    public void SetRandomPath()
    {
        if (grid == null)
        {
            Debug.LogError("Grid not initialized! Call Init(gm) first.");
            return;
        }

        // wybierz losowe dwa pola walkable
        List<Tile> walkable = new List<Tile>();
        foreach (var t in grid)
            if (t.Walkable)
                walkable.Add(t);

        if (walkable.Count < 2)
        {
            Debug.LogWarning("Not enough walkable tiles to make a path!");
            return;
        }

        Tile startTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];
        Tile endTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];

        while (endTile == startTile) // nie ten sam
            endTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];

        FindPath(startTile, endTile);

        startTile.color = new Color32(255, 0, 0, 1);
        endTile.color = new Color32(255, 0, 0, 1);
        gm.gridManager.RefreshAll();

        Debug.Log($"Random start: ({startTile.x},{startTile.y}) | end: ({endTile.x},{endTile.y})");
    }

    public List<Tile> FindPath(Tile startTile, Tile endTile)
    {
        switch (algorithm)
        {
            case PathfindingAlgorithm.AStar:
                return AStar(startTile, endTile);
            // case PathfindingAlgorithm.ACO: // TODO
            // case PathfindingAlgorithm.ABC: // TODO
            default:
                return ACO(startTile, endTile);
        }
    }
    List<Tile> AStar(Tile startTile, Tile endTile)
    {
        return null;
    }

    List<Tile> ACO(Tile startTile, Tile endTile)
    {
        return null;
    }
}
