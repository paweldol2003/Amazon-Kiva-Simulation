using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System

public class PathManager : MonoBehaviour
{
    private GameManager gm;
    //private Tile[,] grid;
    private List<Tile[,]> RTgrid; //Real time grid
    public enum Heading { North = 0, East = 1, South = 2, West = 3 }
    public enum RobotAction { Forward = 0, TurnLeft = 1, TurnRight = 2, Wait = 3 }

    [Header("Controls")]
    public Key nextIterationKey = Key.Space;

    [Header("ACO")]
    public int ants = 40;
    public int iterations = 60;
    [Range(0.1f, 5f)] public float alpha = 1.0f;     // waga feromonów
    [Range(0.1f, 8f)] public float beta = 3.0f;     // waga heurystyki
    [Range(0.01f, 0.99f)] public float evaporation = 0.5f;
    public float Q = 100f;
    public float tau0 = 0.1f;
    public float maxStepsFactor = 0.25f; // limit kroków = maxStepsFactor * (w*h)

    Coroutine acoRoutine;

    public void Init(GameManager gm) => this.gm = gm;
    void Start()
    {
        RTgrid = gm.gridManager.RTgrid;
    }


    public void SetStandardPath(int startStep)
    {
        if (RTgrid[startStep] == null) { Debug.LogError("Grid not initialized!"); return; }

        RobotController robot = gm.robotManager.GetFreeRobot();

        var walkable = new List<Tile>();
        foreach (var t in RTgrid[startStep])
            if (t.Walkable && t.flags.HasFlag(TileFlags.Shelf)) walkable.Add(t);

        Tile startTile = robot.HisTile;
        Tile endTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];

        if (startTile == null || endTile == null)
        {
            Debug.LogWarning("No spawn/goal tiles.");
            return;
        }

        var startHead = robot.Heading;

        Debug.Log($"[ACO] Starting standard path for robot {robot.Id} at step {startStep}");

        acoRoutine = StartCoroutine(ACO_Interactive(
            startTile, endTile, startHead, startStep, robot, path =>
            {
                if (path == null || path.Count == 0)
                {
                    Debug.LogWarning("No path found for robot.");
                    return;
                }

                for (int i = 0; i < path.Count - 1; i++)
                {
                    var node = path[i];
                    var nxtnode = path[i + 1];

                    int step = node.step;
                    if (step < 0 || step >= RTgrid.Count) continue;

                    var gridStep = RTgrid[step];
                    gridStep[node.x, node.y].flags |= TileFlags.Blocked;
                    gridStep[nxtnode.x, nxtnode.y].flags |= TileFlags.Blocked;

                    gm.gridManager.UpdateRTgrid(step, gridStep);
                }

                gm.robotManager.AssignPlanToRobot(robot, path);

            }));
    }

    public void SetSpawnpointPath(int startStep, RobotController robot)
    {
        if (RTgrid[startStep] == null) { Debug.LogError("Grid not initialized!"); return; }

        Tile startTile = robot.HisTile;
        Tile endTile = robot.SpawnTile;
        var startHead = robot.Heading;

        Debug.Log($"[ACO] Starting spawnpoint path for robot {robot.Id} at step {startStep}");

        acoRoutine = StartCoroutine(ACO_Interactive(
            startTile, endTile, startHead, startStep, robot, path =>
            {
                if (path == null || path.Count == 0)
                {
                    Debug.LogError($"No path found for robot {robot.Id} to spawnpoint.");
                    return;
                }

                for (int i = 0; i < path.Count - 1; i++)
                {
                    var node = path[i];
                    var nxtnode = path[i + 1];

                    int step = node.step;
                    if (step < 0 || step >= RTgrid.Count) continue;

                    var gridStep = RTgrid[step];
                    gridStep[node.x, node.y].flags |= TileFlags.Blocked;
                    gridStep[nxtnode.x, nxtnode.y].flags |= TileFlags.Blocked;

                    gm.gridManager.UpdateRTgrid(step, gridStep);
                }

                Debug.LogWarning($"Assigning spawnpoint path to robot {robot.Id}, path size: {path.Count}");
                gm.robotManager.AssignPlanToRobot(robot, path);

            }));
    }


    // ======= MODEL STANU =======
    public struct Node : IEquatable<Node>
    {
        public int x, y;
        public Heading head;
        public RobotAction action; // akcja prowadz¹ca do tego stanu
        public int step;
        public Node(int x, int y, Heading h, RobotAction a, int s ) { this.x = x; this.y = y; this.head = h; this.action = a; this.step = s; }
        public bool Equals(Node other) => x == other.x && y == other.y && head == other.head;
        public override bool Equals(object obj) => obj is Node n && Equals(n);
        public override int GetHashCode() => (x * 73856093) ^ (y * 19349663) ^ ((int)head * 83492791);
    }

    // ======= ACO: tryb interaktywny (iteracja -> kolorowanie -> czekanie na klawisz) =======
    IEnumerator ACO_Interactive(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;   // <-- START pomiaru

        int w = RTgrid[startStep].GetLength(0), h = RTgrid[startStep].GetLength(1);
        int S = w * h * 4; // stany (x,y,heading)
        int A = 4;         // akcje

        // feromony per (stan, akcja)
        float[,] tau = new float[S, A];
        for (int s = 0; s < S; s++)
            for (int a = 0; a < A; a++)
                tau[s, a] = tau0;

        List<Node> bestPath = null;
        int maxSteps = (int)Mathf.Max(32, maxStepsFactor * w * h);
        //Debug.LogWarning($"[Path Manager] maxSteps={maxSteps}");

        //CA£A ITERACJA
        for (int it = 0; it < iterations; it++)
        {
            float tIter = Time.realtimeSinceStartup;

            // 1) Parowanie
            for (int s = 0; s < S; s++)
                for (int a = 0; a < A; a++)
                    tau[s, a] *= (1f - evaporation);

            // 2) Mrówki
            for (int k = 0; k < ants; k++)
            {
                float tAnt = Time.realtimeSinceStartup;

                var nodes = ConstructAntPath( new Node( start.x, start.y, startHead, RobotAction.Wait, startStep), (goal.x, goal.y), tau, maxSteps, startStep);
                if (nodes != null && (bestPath == null || nodes.Count < bestPath.Count)) bestPath = nodes;
                if( (Time.realtimeSinceStartup - tIter) * 1000f > 100)
                {
                    Debug.Log($"[ACO] Ant {k} path-build took {(Time.realtimeSinceStartup - tAnt) * 1000f} ms");
                }
            }
            // 3) Globalna depozycja na best-so-far
            if (bestPath != null && bestPath.Count > 1)
            {
                float deposit = Q / Mathf.Max(1f, bestPath.Count);
                for (int i = 0; i < bestPath.Count - 1; i++)
                {
                    var from = bestPath[i];
                    var to = bestPath[i + 1];
                    var act = InferAction(from, to);
                    if ((int)act >= 0)
                    {
                        int si = StateIndex(from.x, from.y, from.head, w, h);
                        tau[si, (int)act] += deposit;
                    }
                }
            }
            Debug.Log($"[ACO] Iter {it} took {(Time.realtimeSinceStartup - tIter) * 1000f} ms");

        }
        acoRoutine = null;
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[ACO Timer] ACO_Interactive for robot {robot.Id} took {elapsed:F2} ms");

        onDone?.Invoke(bestPath);
        yield break;
    }

    // ======= Pojedyncza mrówka =======
    List<Node> ConstructAntPath(Node start, (int gx, int gy) goal, float[,] tau, int maxSteps, int startStep)
    {
        int w = RTgrid[startStep].GetLength(0), h = RTgrid[startStep].GetLength(1);
        var path = new List<Node>(64) { start };

        Node cur = start;

        for (int s = startStep; s < (maxSteps + startStep); s++)
        {
            if (cur.x == goal.gx && cur.y == goal.gy) return path;

            var actions = AvailableActions(cur);
            if (actions.Count == 0)
            {
                Debug.LogWarning($"[ACO] Brak akcji z ({cur.x},{cur.y},{cur.head}) na s={s}, przed osi¹gniêciem celu ({goal.gx},{goal.gy}).");
                return null;
            }

            int si = StateIndex(cur.x, cur.y, cur.head, w, h);
            float sum = 0f;

            var weights = new float[actions.Count];
            var nexts = new Node[actions.Count];

            gm.gridManager.UpdateRTgrid(s + 1); // upewnij siê, ¿e jest grid na nastêpny krok

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var (nxt, allowed) = Apply(cur, a, w, h);
                if (!allowed)
                {
                    weights[i] = 0f;
                    continue;
                }

                float tau_ = Mathf.Max(1e-6f, tau[si, (int)a]);
                float eta = HeuristicDesirability(cur, nxt, goal); // >0
                float wgt = Mathf.Pow(tau_, alpha) * Mathf.Pow(eta, beta);

                weights[i] = wgt;
                nexts[i] = nxt;
                sum += wgt;
            }

            if (sum <= 0f)
            {
                // wszystkie wagi 0 -> nic sensownego
                Debug.LogWarning($"[ACO] sum==0 dla ({cur.x},{cur.y},{cur.head}) na s={s}, brak sensownego ruchu w stronê ({goal.gx},{goal.gy}).");
                return null;
            }

            // ruletka
            float r = UnityEngine.Random.value * sum;
            float acc = 0f;
            int chosen = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    chosen = i;
                    break;
                }
            }

            cur = nexts[chosen];
            path.Add(cur);
            //Debug.Log($"Node = {cur.x}, {cur.y}, {cur.head}");
        }
        return null;
    }


    // ======= Akcje / przejœcia =======
    List<RobotAction> AvailableActions(Node s)
    {
        // wszystkie 4 akcje; ograniczenia s¹ w Apply()
        return new List<RobotAction> {
            RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait
        };
    }

    (Node next, bool allowed) Apply(Node s, RobotAction a, int w, int height)
    {
        bool walkable = true;
        switch (a)
        {
            case RobotAction.TurnLeft:
                {
                    return (new Node(s.x, s.y, TurnLeft(s.head), RobotAction.TurnLeft, s.step + 1), true);
                }

            case RobotAction.TurnRight:
                {
                    return (new Node(s.x, s.y, TurnRight(s.head), RobotAction.TurnRight, s.step + 1), true);
                }
            case RobotAction.Wait:
                {
                    return (new Node(s.x, s.y, s.head, RobotAction.Wait, s.step + 1), true);
                }

            case RobotAction.Forward:
                {
                    var (nx, ny) = ForwardPos(s.x, s.y, s.head);

                    bool inBounds = nx >= 0 && ny >= 0 && nx < w && ny < height;

                    if (!inBounds)
                        return (s, false);
                    else
                    {
                        walkable = RTgrid[s.step + 1][nx, ny].Walkable;
                        if (!walkable)
                            return (s, false);
                    }
                    return (new Node(nx, ny, s.head, RobotAction.Forward, s.step + 1), true);
                }
        }
        return (s, false);
    }

    Heading TurnLeft(Heading h) => (Heading)(((int)h + 3) & 3);
    Heading TurnRight(Heading h) => (Heading)(((int)h + 1) & 3);

    (int x, int y) ForwardPos(int x, int y, Heading h)
    {
        switch (h)
        {
            case Heading.North: return (x, y + 1);
            case Heading.East: return (x + 1, y);
            case Heading.South: return (x, y - 1);
            case Heading.West: return (x - 1, y);
        }
        return (x, y);
    }


    // ======= Heurystyka / indeksy / pomocnicze =======
    float HeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
    {
        // im bli¿ej celu tym lepiej, delikatna premia za „patrzenie” w kierunku celu
        float curManhattan = Mathf.Abs(c.x - goal.gx) + Mathf.Abs(c.y - goal.gy);
        float nexManhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);
        float facing = 1f;

        int dx = goal.gx - n.x, dy = goal.gy - n.y;
        if (Mathf.Abs(dx) > Mathf.Abs(dy))
            facing = (n.head == Heading.East && dx > 0) || (n.head == Heading.West && dx < 0) ? 1.25f : 1f;
        else if (Mathf.Abs(dy) > 0)
            facing = (n.head == Heading.North && dy > 0) || (n.head == Heading.South && dy < 0) ? 1.25f : 1f;
        return curManhattan - nexManhattan + 1 + facing / ((nexManhattan + 2f)*2);
    }

    int StateIndex(int x, int y, Heading h, int w, int hgt)
    {
        // (x,y,heading) -> [0 .. w*h*4)
        return ((y * w) + x) * 4 + (int)h;
    }

    RobotAction InferAction(Node from, Node to)
    {
        if (from.x == to.x && from.y == to.y)
        {
            if (to.head == TurnLeft(from.head)) return RobotAction.TurnLeft;
            if (to.head == TurnRight(from.head)) return RobotAction.TurnRight;
            if (to.head == from.head) return RobotAction.Wait;
            return (RobotAction)(-1);
        }
        var (fx, fy) = ForwardPos(from.x, from.y, from.head);
        if (fx == to.x && fy == to.y && to.head == from.head) return RobotAction.Forward;
        return (RobotAction)(-1);
    }
}
