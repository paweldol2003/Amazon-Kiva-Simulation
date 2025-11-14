using System.Collections.Generic;
using UnityEngine;

// Alias na Twoje enuma z PathManager
using Heading = PathManager.Heading;
using RobotAction = PathManager.RobotAction;

public enum RobotStatus { Free = 0, Busy = 1 }

public struct RobotInstruction
{
    public int step;
    public RobotAction action;
    public Vector2Int pos;   // docelowa pozycja po tej akcji
    public Heading head;     // heading po tej akcji
    public RobotInstruction(int step, RobotAction action, Vector2Int pos, Heading head)
    {
        this.step = step; this.action = action; this.pos = pos; this.head = head;
    }
}

public class RobotController : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private Vector2Int gridPos;
    [SerializeField] private Heading heading = Heading.North;
    [SerializeField] private RobotStatus status = RobotStatus.Free;
    [SerializeField] private Tile hisTile;
    //[SerializeField] private Tile spawnTile;

    [Header("Visual")]
    [SerializeField] private float visualHeight = 0.5f; // Y robota w œwiecie

    // Id (np. nadawany przez RobotManager)
    public int Id { get; private set; }

    // W³aœciwoœci tylko-do-odczytu
    public Vector2Int GridPos => gridPos;
    public Heading Heading => heading;
    public RobotStatus Status => status;
    public Tile HisTile => hisTile;
    //public Tile SpawnTile { get; private set; } 

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
        //spawnTile = startTile;
    }

    public void SetHeading(Heading h) => heading = h;
    public void SetGridPos(Vector2Int p) => gridPos = p;
    public void SetHisTile(Tile t) => hisTile = t;

    public void SetBusy() => status = RobotStatus.Busy;
    public void SetFree()
    {
        status = RobotStatus.Free;
        plan.Clear();
    }

    public void AssignPlan(IEnumerable<RobotInstruction> instructions)
    {
        plan.Clear();
        foreach (var ins in instructions) plan.Enqueue(ins);
        status = plan.Count > 0 ? RobotStatus.Busy : RobotStatus.Free;
    }

    /// <summary>
    /// Wykonuje instrukcjê przypisan¹ dok³adnie do podanego stepa.
    /// Ustawia heading, gridPos oraz pozycjê world-space (teleport lub p³ynnie).
    /// Zwraca true, gdy wykona³ ruch.
    /// </summary>
    public bool TryExecuteStep(int step, float cellSize, Vector2 origin, float moveTime = 0f)
    {
        if (plan.Count == 0)
        {
            Debug.LogWarning($"[RobotController:{name}] No plan. Status={status}, requestedStep={step}");
            status = RobotStatus.Free;
            return false;
        }

        var ins = plan.Peek();
        if (ins.step != step)
        {
            Debug.Log($"[RobotController:{name}] Step mismatch: requested={step}, nextPlanStep={ins.step} (action={ins.action}, pos={ins.pos}, head={ins.head})");
            return false;
        }

        // zdejmij i zastosuj
        plan.Dequeue();
        heading = ins.head;
        gridPos = ins.pos;

        Vector3 target = GridToWorld(ins.pos, cellSize, origin, visualHeight);
        Debug.Log($"[RobotController:{name}] EXEC step={step} -> pos={gridPos} world={target} action={ins.action} head={heading} moveTime={moveTime}");

        if (moveTime > 0f) StartCoroutine(MoveSmooth(target, moveTime));
        else transform.position = target;

        if (plan.Count == 0) status = RobotStatus.Free;
        return true;
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
}
