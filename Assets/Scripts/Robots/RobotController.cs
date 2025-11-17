using System.Collections.Generic;
using UnityEngine;

// Alias na Twoje enuma z PathManager
using Heading = PathManager.Heading;
using RobotAction = PathManager.RobotAction;

public enum RobotStatus { Free = 0, BusyWithPackage = 1,  BusyWithoutPackage = 2 }

public struct RobotInstruction
{
    public int step;
    public RobotAction action;
    public Tile tile;  // target tile after this action
    public Vector2Int pos;   // target grid position after this action
    public Heading head;     // heading after this action

    public RobotInstruction(int step, RobotAction action, Tile tile, Heading head)
    {
        this.step = step;
        this.action = action;
        this.tile = tile;
        this.pos = new Vector2Int(tile.x, tile.y);
        this.head = head;
    }
}

public class RobotController : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private Vector2Int gridPos;
    [SerializeField] private Heading heading = Heading.North;
    [SerializeField] private RobotStatus status = RobotStatus.Free;
    [SerializeField] private Tile hisTile;
    [SerializeField] private Tile spawnTile;   // <- spawn tile przypisany przy inicjalizacji

    [Header("Visual")]
    [SerializeField] private float visualHeight = 0.5f; // Y robota w świecie

    // Id (np. nadawany przez RobotManager)
    public int Id { get; private set; }

    // Read-only properties
    public Vector2Int GridPos => gridPos;
    public Heading Heading => heading;
    public RobotStatus Status => status;
    public Tile HisTile => hisTile;
    public Tile SpawnTile => spawnTile;   // <- expose spawn tile

    // Plan ruchu (kolejka instrukcji)
    private readonly Queue<RobotInstruction> plan = new Queue<RobotInstruction>();
    public bool HasPlan => plan.Count > 0;

    // --- API ---
    public void Init(int id, Vector2Int startPos, Heading startHeading, Tile startTile)
    {
        Id = id;
        gridPos = startPos;
        heading = startHeading;
        status = RobotStatus.Free;

        hisTile = startTile;
        spawnTile = startTile;   // <- zapamiętujemy kafel startowy jako spawn
    }

    public void SetHeading(Heading h) => heading = h;
    public void SetGridPos(Vector2Int p) => gridPos = p;
    public void SetHisTile(Tile t) => hisTile = t;

    // opcjonalnie, gdybyś kiedyś chciał zmienić spawn z zewnątrz:
    public void SetSpawnTile(Tile t) => spawnTile = t;

    public void SetBusyWithPackage() => status = RobotStatus.BusyWithPackage;
    public void SetBusyWithoutPackage() => status = RobotStatus.BusyWithoutPackage;

    public void SetFree()
    {
        status = RobotStatus.Free;
        plan.Clear();
    }

    public void AssignPlan(IEnumerable<RobotInstruction> instructions)
    {
        plan.Clear();
        foreach (var ins in instructions) plan.Enqueue(ins);
    }

    /// <summary>
    /// Executes instruction assigned exactly to the given step.
    /// Updates heading, gridPos and world-space position.
    /// Returns true when movement was executed.
    /// </summary>
    public int TryExecuteStep(int step, float cellSize, Vector2 origin, float moveTime = 0f)
    {
        if (plan.Count == 0)
        {
                status = RobotStatus.Free;
                return 0;
        }

        var ins = plan.Peek();
        if (ins.step != step)
        {
            Debug.Log($"[RobotController:{name}] Step mismatch: requested={step}, nextPlanStep={ins.step} (action={ins.action}, pos={ins.pos}, head={ins.head})");
            return 0;
        }

        // pop and apply
        plan.Dequeue();
        heading = ins.head;
        gridPos = ins.pos;
        hisTile = ins.tile;
        status = RobotStatus.BusyWithPackage;

        Vector3 target = GridToWorld(ins.pos, cellSize, origin, visualHeight);
        Debug.Log($"[RobotController:{name}] EXEC step={step} -> pos={gridPos} action={ins.action} head={heading} moveTime={moveTime}");

        if (ins.action == RobotAction.TurnLeft)
        {
            StartCoroutine(RotateSmooth(target, moveTime, -90));
        }
        else if (ins.action == RobotAction.TurnRight)
        {
            StartCoroutine(RotateSmooth(target, moveTime, 90));
        }

        if (moveTime > 0f) StartCoroutine(MoveSmooth(target, moveTime));
        else transform.position = target;

        if (plan.Count == 0)
        {
            bool atSpawn =
                (hisTile != null && (hisTile.flags & TileFlags.Spawn) != 0);
            // albo: bool atSpawn = (gridPos == spawnPos);

            if (atSpawn)
            {
                // wrócił na spawn → jest wolny
                status = RobotStatus.Free;
                return 0;  // nic nie trzeba planować
            }
            else
            {
                // skończył misję, ale nie jest na spawnie → prośba o plan powrotny
                status = RobotStatus.BusyWithoutPackage;
                return 2;  // Return to spawn request
            }
        }
        return 1;
    }

    private Vector3 GridToWorld(Vector2Int p, float cellSize, Vector2 origin, float y)
    {
        float wx = origin.x + p.x * cellSize + cellSize * 0.5f;
        float wz = origin.y + p.y * cellSize + cellSize * 0.5f;
        return new Vector3(wx, y, wz);
    }

    private System.Collections.IEnumerator MoveSmooth(Vector3 target, float t)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < t)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / t);
            transform.position = Vector3.Lerp(start, target, u);
            yield return null;
        }
        transform.position = target;
    }

    private System.Collections.IEnumerator RotateSmooth(Vector3 targetPos, float t, float angle)
    {
        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(0f, angle, 0f);
        float elapsed = 0f;
        while (elapsed < t)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / t);
            transform.rotation = Quaternion.Slerp(startRot, endRot, u);
            yield return null;
        }
        transform.rotation = endRot;
    }
}
