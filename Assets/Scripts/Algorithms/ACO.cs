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
    public float maxStepsFactor = 0.25f; // limit kroków = maxStepsFactor * (w*h)

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
        int maxSteps = (int)Mathf.Max(32, maxStepsFactor * w * h);
        Debug.LogWarning($"[Path Manager] maxSteps={maxSteps}");

        //CA£A ITERACJA
        for (int it = 0; it < acoiterations; it++)
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
                var nodes = ConstructAntPath(new Node(start.x, start.y, startHead, RobotAction.Wait, startStep), (goal.x, goal.y), tau, maxSteps, startStep, checktimers);
                if (nodes != null && (bestPath == null || nodes.Count < bestPath.Count)) bestPath = nodes;

                //if(nodes != null) Debug.Log($"[ACO] Ant {k} found path of length {nodes.Count} in iteration {it} (best so far: {bestPath.Count})");
                //else Debug.Log($"[ACO] Ant {k} found NO PATH in iteration {it})");

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
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[ACO Timer] ACO_Interactive for robot {robot.Id} took {elapsed:F2} ms");

        onDone?.Invoke(bestPath);
        yield break;
    }

    // ======= Pojedyncza mrówka =======
    List<Node> ConstructAntPath(
                                Node start,
                                (int gx, int gy) goal,
                                float[,] tau,
                                int maxSteps,
                                int startStep,
                                bool checktimers)
    {
        int w = RTgrid[startStep].GetLength(0), h = RTgrid[startStep].GetLength(1);
        var path = new List<Node>(64) { start };

        Node cur = start;

        float antStart = 0f;
        if (checktimers)
            antStart = Time.realtimeSinceStartup;

        for (int s = startStep; s < (maxSteps + startStep); s++)
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

            var actions = new List<RobotAction> { RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait };
            int si = StateIndex(cur.x, cur.y, cur.head, w, h);
            float sum = 0f;

            var weights = new float[actions.Count];
            var nexts = new Node[actions.Count];

            gm.gridManager.UpdateRTgrid(s + 1);

            for (int i = 0; i < actions.Count; i++)
            {
                var a = actions[i];
                var (nxt, allowed) = Apply(cur, a);
                if (!allowed)
                {
                    weights[i] = 0f;
                    continue;
                }

                float tau_ = Mathf.Max(1e-6f, tau[si, (int)a]);
                float eta = HeuristicDesirability(cur, nxt, goal);
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
}
