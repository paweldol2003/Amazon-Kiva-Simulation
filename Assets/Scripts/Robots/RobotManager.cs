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
    public float visualHeight = 0.14f;

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
                rc.visualHeight = visualHeight;


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
        // zlicz ile robotów jest wolnych
        int freeCount = 0;
        for (int i = 0; i < robots.Count; i++)
            if (robots[i] != null && robots[i].Status == RobotStatus.Free)
                freeCount++;

        if (freeCount == 0)
            return null;

        // losujemy indeks wœród WOLNYCH robotów
        int target = UnityEngine.Random.Range(0, freeCount);

        // drugi przebieg — zwracamy tego losowego
        for (int i = 0; i < robots.Count; i++)
        {
            if (robots[i] != null && robots[i].Status == RobotStatus.Free)
            {
                if (target == 0)
                    return robots[i];
                target--;
            }
        }

        return null;
    }

    /// <summary>
    /// Konwersja œcie¿ki ACO (Node) na plan robota i wgranie do wskazanego robota.
    /// </summary>
    //public void AssignPlanToRobot(RobotController robot, List<PathManager.Node> path)
    //{
    //    if (!robot) { Debug.LogWarning("[RobotManager] AssignPlanToRobot: robot == null"); return; }
    //    if (path == null || path.Count == 0) { Debug.LogWarning("[RobotManager] Pusta œcie¿ka."); return; }

    //    var list = new List<RobotInstruction>(path.Count);
    //    foreach (var n in path)
    //    {
    //        list.Add(new RobotInstruction(
    //            n.step,
    //            n.action,
    //            RTgrid[n.step][n.x, n.y],
    //            n.head
    //        ));
    //    }
    //    robot.AssignPlan(list);
    //    Debug.Log($"[RobotManager] Wgrano plan ({list.Count}) do {robot.name}.");
    //}
    //// W pliku RobotManager.cs

    public void AssignPlanToRobot(RobotController robot, List<PathManager.Node> path)
    {
        if (!robot) { Debug.LogWarning("[RobotManager] AssignPlanToRobot: robot == null"); return; }
        if (path == null || path.Count == 0) { Debug.LogWarning("[RobotManager] Pusta œcie¿ka."); return; }

        // 1. ZWOLNIENIE STARTU (Logika: Wyjazd)
        // Pobieramy startowy punkt œcie¿ki. W tym kroku robot jeszcze tam jest, 
        // ale w kroku nastêpnym (step + 1) ju¿ go nie bêdzie.
        // UWAGA: Startowy node œcie¿ki to zazwyczaj pozycja, w której robot JEST aktualnie.
        var startNode = path[0];

        // Od momentu startu + 1, stare miejsce jest wolne "na zawsze" (dopóki ktoœ inny nie wjedzie)
        gm.gridManager.FreeTileFuture(startNode.x, startNode.y, startNode.step + 1);


        // 2. REZERWACJA PRZEJAZDU (Logika: Jazda)
        // Iterujemy przez ca³¹ œcie¿kê, rezerwuj¹c pola w konkretnych momentach czasu.
        // Tworzymy listê instrukcji dla robota.
        var list = new List<RobotInstruction>(path.Count);

        for (int i = 0; i < path.Count; i++)
        {
            var n = path[i];

            // Dodajemy instrukcjê dla robota (Twoja stara logika)
            list.Add(new RobotInstruction(
                n.step,
                n.action,
                RTgrid[n.step][n.x, n.y],
                n.head
            ));

            // AKTUALIZACJA RTGRID:
            // Rezerwujemy to pole w tym konkretnym kroku czasu.
            // Dziêki temu inne roboty planuj¹ce w przysz³oœci bêd¹ wiedzia³y, ¿e w kroku 'n.step' tu jest t³ok.
            gm.gridManager.ReserveSpecificStep(n.x, n.y, n.step);
            if (i + 1 < path.Count)
            {
                var nxt = path[i + 1];
                gm.gridManager.ReserveSpecificStep(nxt.x, nxt.y, n.step);
            }

        }

        // 3. ZABLOKOWANIE CELU (Logika: Parkowanie)
        // Robot koñczy trasê w ostatnim punkcie. Od tego momentu a¿ do koñca czasu pole musi byæ zajête.
        var endNode = path[path.Count - 1];
        gm.gridManager.BlockTileFuture(endNode.x, endNode.y, endNode.step);


        // 4. Finalizacja przypisania planu
        robot.AssignPlan(list);
        Debug.Log($"[RobotManager] Wgrano plan ({list.Count}) do {robot.name}. Zaktualizowano RTGrid.");
    }


    public void AssignStandardCyclePath(int currentStep)
    {
        RobotController robot = gm.robotManager.GetFreeRobot();
        if (robot == null) return;

        robot.AssignDestination(RobotDestination.ToShelf);
        robot.AssignDestination(RobotDestination.ToTP);
        robot.AssignDestination(RobotDestination.ToShelf);
        robot.AssignDestination(RobotDestination.ToSpawn);
    }
    public void AssignShelfPath(int currentStep)
    {

        RobotController robot = gm.robotManager.GetFreeRobot();
        if (robot == null) return;

        robot.AssignDestination(RobotDestination.ToShelf);
        robot.AssignDestination(RobotDestination.ToSpawn);
    }
    public void AssignTransferPointPath(int currentStep)
    {
        RobotController robot = gm.robotManager.GetFreeRobot();
        if (robot == null) return;

        robot.AssignDestination(RobotDestination.ToTP);
        robot.AssignDestination(RobotDestination.ToSpawn);
    }
    /// <summary>
    /// Uruchamia wykonanie instrukcji zaplanowanych na dany step (dla wszystkich robotów).
    /// </summary>
    public void MoveAllRobots(int step, float? moveTimeOverride = null)
    {
        float cs = gm.gridManager.cellSize;
        Vector2 org = gm.gridManager.origin;
        float t = moveTimeOverride ?? defaultMoveTime;

        foreach (var robot in robots)
        {
            var result = robot.TryExecuteStep(step, cs, org, t);

        }
        List < RobotController > randrobots = new List<RobotController>(robots);
        while (randrobots.Count > 0)
        {
            var rndrbt = randrobots[UnityEngine.Random.Range(0, randrobots.Count)];
            randrobots.Remove(rndrbt);
            //Przypisanie nowego zadania w zale¿noœci od statusu robota
            if (rndrbt.destinations.TryPeek(out var dest))
            {
                var lastPos = rndrbt.lastPlanElement;
                //rndrbt.destinations.Dequeue();
                int startStep = lastPos.step + 1;
                if (dest == RobotDestination.ToSpawn)
                {
                    gm.pathManager.SetSpawnpointPath(startStep, rndrbt);
                }
                else if (dest == RobotDestination.ToTP)
                {
                    gm.pathManager.SetTransferPointPath(startStep, rndrbt);
                }
                else if (dest == RobotDestination.ToShelf)
                {
                    gm.pathManager.SetShelfPath(startStep, rndrbt);
                }
                return;
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
