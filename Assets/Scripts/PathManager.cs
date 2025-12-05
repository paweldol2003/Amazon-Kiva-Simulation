using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System

public partial class PathManager : MonoBehaviour
{
    private GameManager gm;
    //private Tile[,] grid;
    private List<Tile[,]> RTgrid; //Real time grid
    public enum Heading { North = 0, East = 1, South = 2, West = 3 }
    public enum RobotAction { Forward = 0, TurnLeft = 1, TurnRight = 2, Wait = 3 }

    private enum AlgorithmMode { ACO = 0, PSO = 1, Firefly = 2, All = 3 }
    private AlgorithmMode algorithmMode = AlgorithmMode.Firefly;

    [Header("Controls")]
    public Key nextIterationKey = Key.Space;
    public Key switchToThirdAlgo = Key.P;
    public Key switchToACO = Key.O;
    public Key switchToPSO = Key.I;
    public Key switchToAll = Key.U;
    public void Init(GameManager gm) => this.gm = gm;
    void Start() { RTgrid = gm.gridManager.RTgrid; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[switchToACO].wasPressedThisFrame) algorithmMode = AlgorithmMode.ACO;
        else if (kb[switchToPSO].wasPressedThisFrame) algorithmMode = AlgorithmMode.PSO;
        else if (kb[switchToThirdAlgo].wasPressedThisFrame) algorithmMode = AlgorithmMode.Firefly;
        else if (kb[switchToAll].wasPressedThisFrame) algorithmMode = AlgorithmMode.All;
    }


    public void SetShelfPath(int startStep, RobotController robot = null)
    {
        if (RTgrid[startStep] == null) { Debug.LogError("Grid not initialized!"); return ; }

        Tile startTile;
        Heading startHead;
        if (robot == null)
        {
            robot = gm.robotManager.GetFreeRobot();
            startTile = robot.HisTile;
            startHead = robot.Heading;
        }
        else
        {
            startTile = robot.lastPlanElement.tile;
            startHead = robot.lastPlanElement.head;
        }

        var walkableShelf = new List<Tile>();
        foreach (var t in RTgrid[startStep]) if (t.Walkable && t.flags.HasFlag(TileFlags.Shelf)) walkableShelf.Add(t);

        Tile endTile = walkableShelf[UnityEngine.Random.Range(0, walkableShelf.Count)];

        switch (algorithmMode)
        {
            case AlgorithmMode.ACO:
                Debug.Log($"[ACO] Starting shelf path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                break;

            case AlgorithmMode.PSO:
                Debug.Log($"[PSO] Starting shelf path for robot {robot.Id} at step {startStep}");
                endTile = walkableShelf[64];
                PSO_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting shelf path for robot {robot.Id} at step {startStep}");
                //endTile = walkableShelf[64];

                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting shelf path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                Bacteria_Start(startTile, endTile, startHead, startStep, robot);
                break;
        }

            //ACO_Start(startTile, endTile, startHead, startStep, robot);
    }

    public void SetSpawnpointPath(int startStep, RobotController robot)
    {
        if (RTgrid[startStep] == null) { Debug.LogError("Grid not initialized!"); return ; }

        Tile startTile;
        Heading startHead;
        if (robot == null)
        {
            robot = gm.robotManager.GetFreeRobot();
            startTile = robot.HisTile;
            startHead = robot.Heading;
        }
        else
        {
            startTile = robot.lastPlanElement.tile;
            startHead = robot.lastPlanElement.head;
        }

        Tile endTile = robot.SpawnTile;

        switch (algorithmMode)
        {
            case AlgorithmMode.ACO:
                Debug.Log($"[ACO] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.PSO:
                Debug.Log($"[PSO] Starting shelf path for robot {robot.Id} at step {startStep}");
                PSO_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                Bacteria_Start(startTile, endTile, startHead, startStep, robot);
                break;
        }
             //ACO_Start(startTile, endTile, startHead, startStep, robot);
    }

    public void SetTransferPointPath(int startStep, RobotController robot = null, Tile targetTP = null)
    {
        if (RTgrid[startStep] == null) { Debug.LogError("Grid not initialized!"); return ; }

        Tile startTile;
        Heading startHead;
        if (robot == null)
        {
            robot = gm.robotManager.GetFreeRobot();
            startTile = robot.HisTile;
            startHead = robot.Heading;
        }
        else
        {
            startTile = robot.lastPlanElement.tile;
            startHead = robot.lastPlanElement.head;
        }
            
        if (targetTP == null)
        {
            var walkableTP = new List<Tile>();
            foreach (var t in RTgrid[startStep]) if (t.Walkable && t.flags.HasFlag(TileFlags.TransferPoint)) walkableTP.Add(t);
            targetTP = walkableTP[UnityEngine.Random.Range(0, walkableTP.Count)];
        }
        Tile endTile = targetTP;
        switch(algorithmMode)
        {
            case AlgorithmMode.ACO:
                Debug.Log($"[ACO] Starting transfer point path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.PSO:
                Debug.Log($"[PSO] Starting shelf path for robot {robot.Id} at step {startStep}");
                PSO_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting transfer point path for robot {robot.Id} at step {startStep}");
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting transfer point path for robot {robot.Id} at step {startStep}");
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                Bacteria_Start(startTile, endTile, startHead, startStep, robot);
                break;
        }
        //ACO_Start(startTile, endTile, startHead, startStep, robot);
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


    // ======= Akcje / przejœcia =======
    (Node next, bool allowed) Apply(Node s, RobotAction a)
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

                    bool inBounds = nx >= 0 && ny >= 0 && nx < RTgrid[0].GetLength(0) && ny < RTgrid[0].GetLength(1);

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
            facing = (n.head == Heading.East && dx > 0) || (n.head == Heading.West && dx < 0) ? 1.2f : 1f;
        else if (Mathf.Abs(dy) > 0)
            facing = (n.head == Heading.North && dy > 0) || (n.head == Heading.South && dy < 0) ? 1.2f : 1f;
        return curManhattan - nexManhattan + 1 + facing / ((nexManhattan + 2f)*2);
    }

    int StateIndex(int x, int y, Heading h, int w, int hgt)
    {
        // (x,y,heading) -> [0 .. w*h*4), indeks 1D
        return ((y * w) + x) * 4 + (int)h;
    }

    RobotAction InferAction(Node from, Node to)
    {
        // okreœla jak¹ akcjê trzeba wykonaæ, aby przejœæ ze stanu from do stanu to
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
