using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // New Input System

public class PathManager : MonoBehaviour
{
    private GameManager gm;
    private Tile[,] grid;

    public enum Heading { North = 0, East = 1, South = 2, West = 3 }
    public enum RobotAction { Forward = 0, TurnLeft = 1, TurnRight = 2, Wait = 3 }

    [Header("Controls")]
    public Key nextIterationKey = Key.Space;

    [Header("Costs")]
    public float forwardBaseCost = 1f;        // bazowy koszt Forward (dojdzie koszt kafelka)
    public float turnCost = 1f;               // koszt skrêtu w miejscu
    public float waitCost = 0.5f;             // koszt czekania
    public bool blockForwardIfNotWalkable = true;

    [Header("ACO")]
    public int ants = 40;
    public int iterations = 60;
    [Range(0.1f, 5f)] public float alpha = 1.0f;     // waga feromonów
    [Range(0.1f, 8f)] public float beta = 3.0f;     // waga heurystyki
    [Range(0.01f, 0.99f)] public float evaporation = 0.5f;
    public float Q = 100f;
    public float tau0 = 0.1f;
    public int maxStepsFactor = 4; // limit kroków = maxStepsFactor * (w*h)

    Coroutine acoRoutine;

    public void Init(GameManager gm) => this.gm = gm;
    void Start() => grid = gm.gridManager.grid;

    // --- Public trigger (losowy start/koniec, startuje tryb iteracyjny na klawisz) ---
    public void SetRandomPath()
    {
        if (grid == null) { Debug.LogError("Grid not initialized!"); return; }

        var walkable = new List<Tile>();
        foreach (var t in grid) if (t.Walkable) walkable.Add(t);
        if (walkable.Count < 2) { Debug.LogWarning("Not enough walkable."); return; }

        var startTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];
        var endTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];
        while (endTile == startTile) endTile = walkable[UnityEngine.Random.Range(0, walkable.Count)];
        var startHead = (Heading)UnityEngine.Random.Range(0, 4);

        // wyczyœæ i zaznacz start/goal
        foreach (var t in grid) t.color = new Color32(0, 0, 0, 255); // czarne t³o
        grid[startTile.x, startTile.y].color = new Color32(255, 64, 64, 255);
        grid[endTile.x, endTile.y].color = new Color32(255, 64, 64, 255);
        gm.gridManager.RefreshAll();

        if (acoRoutine != null) StopCoroutine(acoRoutine);
        acoRoutine = StartCoroutine(ACO_Interactive(startTile, endTile, startHead));
    }

    // ======= MODEL STANU =======
    public struct Node : IEquatable<Node>
    {
        public int x, y;
        public Heading head;
        public Node(int x, int y, Heading h) { this.x = x; this.y = y; this.head = h; }
        public bool Equals(Node other) => x == other.x && y == other.y && head == other.head;
        public override bool Equals(object obj) => obj is Node n && Equals(n);
        public override int GetHashCode() => (x * 73856093) ^ (y * 19349663) ^ ((int)head * 83492791);
    }

    // ======= ACO: tryb interaktywny (iteracja -> kolorowanie -> czekanie na klawisz) =======
    IEnumerator ACO_Interactive(Tile start, Tile goal, Heading startHead)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        int S = w * h * 4; // stany (x,y,heading)
        int A = 4;         // akcje

        // feromony per (stan, akcja)
        float[,] tau = new float[S, A];
        for (int s = 0; s < S; s++)
            for (int a = 0; a < A; a++)
                tau[s, a] = tau0;

        List<Node> bestPath = null;
        float bestLen = float.PositiveInfinity;
        int maxSteps = Mathf.Max(32, maxStepsFactor * w * h);

        for (int it = 0; it < iterations; it++)
        {
            // 1) Parowanie
            for (int s = 0; s < S; s++)
                for (int a = 0; a < A; a++)
                    tau[s, a] *= (1f - evaporation);

            // 2) Mrówki
            for (int k = 0; k < ants; k++)
            {
                var (nodes, totalLen) = ConstructAntPath(
                    new Node(start.x, start.y, startHead),
                    (goal.x, goal.y),
                    tau, maxSteps
                );

                if (nodes != null && totalLen < bestLen)
                {
                    bestLen = totalLen;
                    bestPath = nodes;
                }
            }

            // 3) Globalna depozycja na best-so-far
            if (bestPath != null && bestPath.Count > 1)
            {
                float deposit = Q / Mathf.Max(1f, bestLen);
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

            // 4) Wizualizacja feromonów w kanale R
            ColorByPheromones_R(tau, w, h);

            // podbij start/goal
            grid[start.x, start.y].color = new Color32(255, 64, 64, 255);
            grid[goal.x, goal.y].color = new Color32(255, 64, 64, 255);

            gm.gridManager.RefreshAll();

            // 5) Czekaj na klawisz
            yield return new WaitUntil(() =>
            {
                var kb = Keyboard.current;
                return kb != null;//&& kb[nextIterationKey].wasPressedThisFrame;
            });
        }

        acoRoutine = null;
    }

    // ======= Pojedyncza mrówka =======
    (List<Node>, float) ConstructAntPath(Node start, (int gx, int gy) goal, float[,] tau, int maxSteps)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        var path = new List<Node>(64) { start };
        var visited = new HashSet<Node> { start };

        Node cur = start;
        float len = 0f;

        for (int s = 0; s < maxSteps; s++)
        {
            if (cur.x == goal.gx && cur.y == goal.gy) return (path, len);

            var actions = AvailableActions(cur);
            if (actions.Count == 0) return (null, float.PositiveInfinity);

            int si = StateIndex(cur.x, cur.y, cur.head, w, h);
            float sum = 0f;

            var weights = new float[actions.Count];
            var nexts = new Node[actions.Count];
            var costs = new float[actions.Count];

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var (nxt, stepCost, allowed) = Apply(cur, a);
                if (!allowed) { weights[i] = 0f; continue; }

                if (visited.Contains(nxt)) stepCost += 0.25f; // delikatna kara za pêtle

                float tau_ = Mathf.Max(1e-6f, tau[si, (int)a]);
                float eta = HeuristicDesirability(nxt, goal); // >0
                float wgt = Mathf.Pow(tau_, alpha) * Mathf.Pow(eta, beta);

                weights[i] = wgt; nexts[i] = nxt; costs[i] = stepCost; sum += wgt;
            }

            if (sum <= 0f)
            {
                // ratunkowo: wybierz jak¹kolwiek dozwolon¹ akcjê
                bool moved = false;
                for (int i = 0; i < actions.Count; i++)
                {
                    var (nxt, cst, ok) = Apply(cur, actions[i]);
                    if (ok)
                    {
                        cur = nxt; len += cst; path.Add(cur); visited.Add(cur);
                        moved = true; break;
                    }
                }
                if (!moved) return (null, float.PositiveInfinity);
            }
            else
            {
                // ruletka
                float r = UnityEngine.Random.value * sum, acc = 0f;
                int chosen = 0;
                for (int i = 0; i < weights.Length; i++)
                {
                    acc += weights[i];
                    if (r <= acc) { chosen = i; break; }
                }
                cur = nexts[chosen]; len += costs[chosen]; path.Add(cur); visited.Add(cur);
            }
        }

        return (null, float.PositiveInfinity);
    }

    // ======= Akcje / przejœcia =======
    List<RobotAction> AvailableActions(Node s)
    {
        // wszystkie 4 akcje; ograniczenia s¹ w Apply()
        return new List<RobotAction> {
            RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait
        };
    }

    (Node next, float cost, bool allowed) Apply(Node s, RobotAction a)
    {
        switch (a)
        {
            case RobotAction.TurnLeft:
                return (new Node(s.x, s.y, TurnLeft(s.head)), turnCost, true);

            case RobotAction.TurnRight:
                return (new Node(s.x, s.y, TurnRight(s.head)), turnCost, true);

            case RobotAction.Wait:
                return (new Node(s.x, s.y, s.head), waitCost, true);

            case RobotAction.Forward:
                var (nx, ny) = ForwardPos(s.x, s.y, s.head);
                if (!Inside(nx, ny) || (blockForwardIfNotWalkable && !grid[nx, ny].Walkable))
                    return (s, 0f, false);
                float tileCost = Mathf.Max(1, grid[nx, ny].cost);
                return (new Node(nx, ny, s.head), forwardBaseCost + tileCost, true);
        }
        return (s, 0f, false);
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

    bool Inside(int x, int y)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        return x >= 0 && y >= 0 && x < w && y < h;
    }

    // ======= Heurystyka / indeksy / pomocnicze =======
    float HeuristicDesirability(Node n, (int gx, int gy) goal)
    {
        // im bli¿ej celu tym lepiej, delikatna premia za „patrzenie” w kierunku celu
        float manhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);
        float facing = 1f;

        int dx = goal.gx - n.x, dy = goal.gy - n.y;
        if (Mathf.Abs(dx) > Mathf.Abs(dy))
            facing = (n.head == Heading.East && dx > 0) || (n.head == Heading.West && dx < 0) ? 1.25f : 1f;
        else if (Mathf.Abs(dy) > 0)
            facing = (n.head == Heading.North && dy > 0) || (n.head == Heading.South && dy < 0) ? 1.25f : 1f;

        return facing / (manhattan + 2f);
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

    // ======= Wizualizacja feromonów w kanale R =======
    void ColorByPheromones_R(float[,] tau, int w, int h)
    {
        // sumujemy feromony akcji Forward ze wszystkich headingów dla danego (x,y)
        float[,] score = new float[w, h];
        float maxScore = 1e-6f;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                for (int head = 0; head < 4; head++)
                {
                    int si = StateIndex(x, y, (Heading)head, w, h);
                    s += Mathf.Max(0f, tau[si, (int)RobotAction.Forward]);
                    // (opcjonalnie, lekkie doci¹¿enie zakrêtów)
                    // s += 0.3f * Mathf.Max(0f, tau[si, (int)RobotAction.TurnLeft]);
                    // s += 0.3f * Mathf.Max(0f, tau[si, (int)RobotAction.TurnRight]);
                }
                // opcjonalna log-skala dla lepszej separacji wizualnej:
                // s = Mathf.Log(1f + s);
                score[x, y] = s;
                if (s > maxScore) maxScore = s;
            }

        // malowanie: tylko kana³ R (obstacles ciemne)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!grid[x, y].Walkable)
                {
                    grid[x, y].color = new Color32(40, 0, 0, 255);
                    continue;
                }
                float norm = score[x, y] / maxScore; // 0..1
                byte R = (byte)Mathf.RoundToInt(255f * norm);
                grid[x, y].color = new Color32(R, 0, 0, 255);
            }
    }
}
