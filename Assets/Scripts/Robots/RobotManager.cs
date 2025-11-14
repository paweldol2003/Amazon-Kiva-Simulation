using System.Collections.Generic;
using UnityEngine;

// Alias na enuma z PathManager
using Heading = PathManager.Heading;
using RobotAction = PathManager.RobotAction;

public class RobotManager : MonoBehaviour
{
    private GameManager gm;
    private List<Tile[,]> RTgrid; // Real-time grid (kroki)

    [Header("Robot Setup")]
    public RobotController robotPrefab;
    [Tooltip("Y po³o¿enia robota (musi pasowaæ do sceny)")]
    public float visualHeight = 0.5f;

    [Header("Movement")]
    [Tooltip("Czas p³ynnego ruchu robota miêdzy kaflami (0 = teleport)")]
    public float defaultMoveTime = 0.15f;

    private readonly List<RobotController> robots = new List<RobotController>();
    public IReadOnlyList<RobotController> AllRobots => robots;

    public void Init(GameManager manager) => gm = manager;

    void Start()
    {
        RTgrid = gm.gridManager.RTgrid;

        int id = 0;
        float cs = gm.gridManager.cellSize;
        Vector2 org = gm.gridManager.origin;

        // Spawn 1:1 do spawnpointów w RTgrid[0]
        foreach (var tile in RTgrid[0])
        {
            if ((tile.flags & TileFlags.Spawn) != 0)
            {
                Vector3 spawnPos = GridToWorld(new Vector2Int(tile.x, tile.y), cs, org, visualHeight);

                var rc = Instantiate(robotPrefab, spawnPos, Quaternion.identity);
                rc.gameObject.name = $"Robot_{id}";
                rc.gameObject.tag = "Robot";

                rc.Init(id, new Vector2Int(tile.x, tile.y), Heading.North, tile);
                robots.Add(rc);
                id++;
            }
            else
            {
                //Debug.LogWarning($"[RobotManager] Tile ({tile.x}, {tile.y}) nie jest spawnpointem, pomijam.");
            }
        }

        Debug.Log($"[RobotManager] Utworzono {robots.Count} robotów.");
    }

    // --- Public API ---
    public RobotController GetFreeRobot()
    {
        for (int i = 0; i < robots.Count; i++)
            if (robots[i] && robots[i].Status == RobotStatus.Free) return robots[i];
        return null;
    }

    /// <summary>
    /// Konwersja œcie¿ki ACO (Node) na plan robota i wgranie do wskazanego robota.
    /// </summary>
    public void AssignPlanToRobot(RobotController robot, List<PathManager.Node> path)
    {
        if (!robot) { Debug.LogWarning("[RobotManager] AssignPlanToRobot: robot == null"); return; }
        if (path == null || path.Count == 0) { Debug.LogWarning("[RobotManager] Pusta œcie¿ka."); return; }

        var list = new List<RobotInstruction>(path.Count);
        foreach (var n in path)
        {
            list.Add(new RobotInstruction(
                n.step,
                n.action,
                new Vector2Int(n.x, n.y),
                n.head
            ));
        }
        robot.AssignPlan(list);
        Debug.Log($"[RobotManager] Wgrano plan ({list.Count}) do {robot.name}.");
    }

    /// <summary>
    /// Uruchamia wykonanie instrukcji zaplanowanych na dany step (dla wszystkich robotów).
    /// </summary>
    public void AdvanceRobotsAtStep(int step, float? moveTimeOverride = null)
    {
        float cs = gm.gridManager.cellSize;
        Vector2 org = gm.gridManager.origin;
        float t = moveTimeOverride ?? defaultMoveTime;

        for (int i = 0; i < robots.Count; i++)
            if (robots[i]) robots[i].TryExecuteStep(step, cs, org, t);
    }

    /// <summary>
    /// (opcjonalne) Wymusza pojedynczy krok „co mam w kolejce” dla wszystkich zajêtych robotów.
    /// </summary>
    public void StepAllRobotsInstant()
    {
        float cs = gm.gridManager.cellSize;
        Vector2 org = gm.gridManager.origin;

        for (int i = 0; i < robots.Count; i++)
        {
            var r = robots[i];
            if (r && r.HasPlan)
            {
                // Odczyt top step-a bez jego zdjêcia (jeœli chcesz strict-step, korzystaj z AdvanceRobotsAtStep)
                // Tu robimy po prostu „nastêpny” — teleport:
                // Brak publicznego podgl¹du kolejki -> w praktyce korzystaj z AdvanceRobotsAtStep(...)
                // Zostawiam jako placeholder API.
            }
        }
    }

    public void OnRobotClicked(GameObject robotGO)
    {
        var robot = robotGO.GetComponent<RobotController>();
        if (!robot)
        {
            Debug.LogWarning("[RobotManager] Klikniêto obiekt bez RobotController!");
            return;
        }
        Debug.Log($"[RobotManager] Wybrano {robot.name} | Pos: {robot.GridPos} | Head: {robot.Heading} | Status: {robot.Status}");
    }

    // --- Helpers ---
    private Vector3 GridToWorld(Vector2Int p, float cellSize, Vector2 origin, float y)
    {
        float wx = origin.x + p.x * cellSize + cellSize * 0.5f;
        float wz = origin.y + p.y * cellSize + cellSize * 0.5f;
        return new Vector3(wx, y, wz);
    }
}
