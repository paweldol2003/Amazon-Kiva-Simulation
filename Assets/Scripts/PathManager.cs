using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System

public partial class PathManager : MonoBehaviour
{
    private GameManager gm;
    private List<Tile[,]> RTgrid; //Real time grid
    public enum Heading { North = 0, East = 1, South = 2, West = 3 }
    public enum RobotAction { Forward = 0, TurnLeft = 1, TurnRight = 2, Wait = 3 }

    private enum AlgorithmMode { ACO = 0, Firefly = 2, Camel = 4, All = 5 }
    private AlgorithmMode algorithmMode = AlgorithmMode.All;

    [Header("Controls")]
    public Key nextIterationKey = Key.Space;
    public Key switchToThirdAlgo = Key.P;
    public Key switchToACO = Key.O;
    public Key switchToCamel = Key.I;
    public Key switchToAll = Key.U;
    public void Init(GameManager gm) => this.gm = gm;
    void Start() { RTgrid = gm.gridManager.RTgrid; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb[switchToACO].wasPressedThisFrame) algorithmMode = AlgorithmMode.ACO;
        else if (kb[switchToCamel].wasPressedThisFrame) algorithmMode = AlgorithmMode.Camel;
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
            case AlgorithmMode.Camel:
                Debug.Log($"[Camel] Starting shelf path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting shelf path for robot {robot.Id} at step {startStep}");
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting shelf path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);

                break;
        }
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
            case AlgorithmMode.Camel:
                Debug.Log($"[Camel] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                break;

            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting spawnpoint path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);

                break;
        }
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
            case AlgorithmMode.Camel:
                Debug.Log($"[Camel] Starting transfer point path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.Firefly:
                Debug.Log($"[Firefly] Starting transfer point path for robot {robot.Id} at step {startStep}");
                Firefly_Start(startTile, endTile, startHead, startStep, robot);
                break;
            case AlgorithmMode.All:
                Debug.Log($"[All] Starting transfer point path for robot {robot.Id} at step {startStep}");
                Camel_Start(startTile, endTile, startHead, startStep, robot);
                ACO_Start(startTile, endTile, startHead, startStep, robot);
                Firefly_Start(startTile, endTile, startHead, startStep, robot);

                break;
        }
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

    private static readonly RobotAction[] _cachedActions = new RobotAction[]
    {
    RobotAction.Forward,
    RobotAction.TurnLeft,
    RobotAction.TurnRight,
    RobotAction.Wait
    };


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
}
