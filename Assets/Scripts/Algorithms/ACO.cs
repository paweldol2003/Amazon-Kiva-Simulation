using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("ACO")]
    public int ants = 40;
    public int acoiterations = 60;
    [Range(0.1f, 5f)] public float alpha = 1.0f;     // waga feromonów
    [Range(0.1f, 8f)] public float beta = 3.0f;     // waga heurystyki
    [Range(0.01f, 0.99f)] public float evaporation = 0.5f;
    public float Q = 100f;
    public float tau0 = 0.1f;
    public int ACOMaxSteps = 300;
    void ACO_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        //System.Action<List<Node>> onDone;
        StartCoroutine(ACO_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogError($"No path found for robot {robot.Id} to point.");
                return;
            }
            Debug.LogWarning($"Assigning point path to robot {robot.Id}, path size: {path.Count}");
            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();

        }));
    }
    IEnumerator ACO_Coroutine(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
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
        //CA£A ITERACJA
        int it = 0;
        for (; it < acoiterations; it++)
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
                bool checktimers = (k % 5) == 0; // co pi¹ta mrówka sprawdza czas
                var nodes = ConstructAntPath(new Node(start.x, start.y, startHead, RobotAction.Wait, startStep), (goal.x, goal.y), tau, startStep, checktimers);
                if (nodes != null && (bestPath == null || nodes.Count < bestPath.Count)) bestPath = nodes;
            }
            // 3) Globalna depozycja na best-so-far
            if (bestPath != null && bestPath.Count > 1)
            {
                float deposit = Q / Mathf.Max(1f, bestPath.Count);
                for (int i = 0; i < bestPath.Count - 1; i++)
                {
                    var from = bestPath[i];
                    var to = bestPath[i + 1];
                    var act = ACO_InferAction(from, to);
                    if ((int)act >= 0)
                    {
                        int si = ACO_StateIndex(from.x, from.y, from.head, w, h);
                        tau[si, (int)act] += deposit;
                    }
                }
            }
            Debug.Log($"[ACO] Iter {it} took {(Time.realtimeSinceStartup - tIter) * 1000f} ms");

        }
        foreach (var n in bestPath)
            RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
        gm.gridManager.RefreshAll(startStep);
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[ACO Timer] ACO_Interactive for robot {robot.Id} took {elapsed:F2} ms and {it} iterations");

        onDone?.Invoke(bestPath);
        yield break;
    }

    // ======= Pojedyncza mrówka =======
    List<Node> ConstructAntPath(
                                Node start,
                                (int gx, int gy) goal,
                                float[,] tau,
                                int startStep,
                                bool checktimers)
    {
        int w = RTgrid[startStep].GetLength(0), h = RTgrid[startStep].GetLength(1);
        var path = new List<Node>(64) { start };

        Node cur = start;

        float antStart = 0f;
        if (checktimers)
            antStart = Time.realtimeSinceStartup;

        for (int s = startStep; s < (ACOMaxSteps + startStep); s++)
        {
            if (cur.x == goal.gx && cur.y == goal.gy)
            {
                if (checktimers)
                {
                    float antTime = (Time.realtimeSinceStartup - antStart) * 1000f;
                    Debug.Log($"[CAP] Ant return path, total time: {antTime:F3} ms, step: {s}");
                }
                return path;
            }

            float stepStart = 0f;
            if (checktimers) stepStart = Time.realtimeSinceStartup;

            //var actions = new List<RobotAction> { RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait };
            int si = ACO_StateIndex(cur.x, cur.y, cur.head, w, h);
            float sum = 0f;

            var weights = new float[4];
            var nexts = new Node[4];

            //gm.gridManager.UpdateRTgrid(s + 1);

            for (int i = 0; i < _cachedActions.Length; i++)
            {
                var a = _cachedActions[i];
                var (nxt, allowed) = Apply(cur, a);
                if (!allowed)
                {
                    weights[i] = 0f;
                    continue;
                }

                float tau_ = Mathf.Max(1e-6f, tau[si, (int)a]);
                float eta = ACO_HeuristicDesirability(cur, nxt, goal);
                float wgt = Mathf.Pow(tau_, alpha) * Mathf.Pow(eta, beta);

                weights[i] = wgt;
                nexts[i] = nxt;
                sum += wgt;
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

            if (checktimers)
            {
                float stepTime = (Time.realtimeSinceStartup - stepStart) * 1000f;
                //if (stepTime > 0.1f) Debug.Log($"[CAP] LONG STEP Step {s - startStep}: {stepTime:F3} ms");
            }
        }

        if (checktimers)
        {
            float antTime = (Time.realtimeSinceStartup - antStart) * 1000f;
            //Debug.Log($"[CAP] Ant ended (no path) total time: {antTime:F3} ms");
        }

        return null;
    }

    float ACO_HeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
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
        return curManhattan - nexManhattan + 1 + facing / ((nexManhattan + 2f) * 2);
    }
    RobotAction ACO_InferAction(Node from, Node to)
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

    int ACO_StateIndex(int x, int y, Heading h, int w, int hgt)
    {
        // (x,y,heading) -> [0 .. w*h*4), indeks 1D
        return ((y * w) + x) * 4 + (int)h;
    }
}
