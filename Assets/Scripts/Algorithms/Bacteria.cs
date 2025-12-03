using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("Bacteria")]
    public int bacterias = 40;
    public int maxSwimmingSteps = 6;
    public int maxChemotaxisSteps = 30;
    public int reproductionIterations = 40;
    public float dispersalProbability = 0.2f;
    public int dispersalCycles = 3;

    // ============================================================
    // ENTRY POINT
    // ============================================================
    void Bacteria_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        Debug.Log($"[BFOA] Starting bacterial optimization for robot {robot.Id}");

        StartCoroutine(Bacteria_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogError($"[BFOA] Failed to find ANY path for robot {robot.Id}");
                return;
            }

            Debug.Log($"[BFOA] Path found for robot {robot.Id}, length={path.Count}");

            for (int i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];

                if (a.step >= 0 && a.step < RTgrid.Count)
                {
                    RTgrid[a.step][a.x, a.y].flags |= TileFlags.Blocked;
                    RTgrid[a.step][b.x, b.y].flags |= TileFlags.Blocked;
                    gm.gridManager.UpdateRTgrid(a.step, RTgrid[a.step]);
                }
            }

            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();
        }));
    }

    // ============================================================
    // MAIN BFOA LOOP
    // ============================================================
    IEnumerator Bacteria_Coroutine(Tile start, Tile goal, Heading startHead, int startStep,
                                   RobotController robot, Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;

        int w = RTgrid[startStep].GetLength(0);
        int h = RTgrid[startStep].GetLength(1);

        Debug.Log($"[BFOA] Creating initial population of {bacterias} bacteria");

        List<PathCandidate> population = new List<PathCandidate>(bacterias);

        for (int i = 0; i < bacterias; i++)
            population.Add(ThreeStepStart(start, startHead, startStep, w, h, goal));

        PathCandidate best = null;

        for (int ed = 0; ed < dispersalCycles; ed++)
        {
            Debug.Log($"[BFOA] Dispersal cycle {ed + 1}/{dispersalCycles}");

            for (int re = 0; re < reproductionIterations; re++)
            {
                Debug.Log($"[BFOA]  Reproduction cycle {re + 1}/{reproductionIterations}");

                for (int i = 0; i < bacterias; i++)
                {
                    var current = population[i];
                    float bestScore = current.cost;

                    for (int c = 0; c < maxChemotaxisSteps; c++)
                    {
                        var tumble = DoTumble(current, goal);

                        Debug.Log($"[BFOA]   Tumble: oldScore={bestScore:F3}, newScore={tumble.cost:F3}");

                        if (tumble.cost > bestScore)
                        {
                            Debug.Log("[BFOA]    Tumble improved score, starting swim");
                            current = tumble;
                            bestScore = tumble.cost;

                            int swimCount = 0;

                            for (int s = 0; s < maxSwimmingSteps; s++)
                            {
                                var swim = DoSwim(current, goal);
                                if (swim.cost > bestScore)
                                {
                                    swimCount++;
                                    current = swim;
                                    bestScore = swim.cost;
                                }
                                else
                                {
                                    break;
                                }
                            }

                            Debug.Log($"[BFOA]    Swim ended after {swimCount} forward steps");
                        }
                    }

                    population[i] = current;

                    if (best == null || current.cost > best.cost)
                    {
                        best = current;
                        Debug.Log($"[BFOA]  New global best score={best.cost:F3}, length={best.path.Count}");
                    }
                }

                population.Sort((a, b) => b.cost.CompareTo(a.cost)); // sort descending
                int half = population.Count / 2;

                Debug.Log("[BFOA] Reproduction: Cloning best half into worst half");

                for (int i = half; i < population.Count; i++)
                    population[i] = population[i - half].Clone();
            }

            Debug.Log("[BFOA] Dispersal phase");

            for (int i = 0; i < population.Count; i++)
            {
                if (UnityEngine.Random.value < dispersalProbability)
                {
                    population[i] = ThreeStepStart(start, startHead, startStep, w, h, goal);
                    Debug.Log($"[BFOA]  Dispersed bacterium {i}");
                }
            }
        }

        float t1 = Time.realtimeSinceStartup;
        Debug.LogWarning($"[BFOA] Finished in {(t1 - t0) * 1000f:F1} ms. BestLength={best?.path.Count}");

        onDone(best?.path);
        yield return null;
    }

    // ============================================================
    // PATH CANDIDATE CLASS
    // ============================================================
    class PathCandidate
    {
        public List<Node> path;
        public float cost;
        public PathCandidate(List<Node> p, float c) { path = p; cost = c; }
        public PathCandidate Clone() => new PathCandidate(new List<Node>(path), cost);
    }

    // ============================================================
    // 3-STEP RANDOM START
    // ============================================================
    PathCandidate ThreeStepStart(Tile start, Heading startHead, int startStep,
                                 int w, int h, Tile goal)
    {
        List<Node> p = new List<Node>();
        Node n = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        p.Add(n);

        Debug.Log("[BFOA] Creating 3-step start");

        for (int i = 0; i < 3; i++)
        {
            RobotAction a = (RobotAction)UnityEngine.Random.Range(0, 4);
            var (nx, ok) = Apply(n, a);
            if (!ok)
            {
                Debug.Log("[BFOA]  Start step blocked");
                continue;
            }

            p.Add(nx);
            n = nx;
        }

        float score = Evaluate(p, (goal.x, goal.y));
        Debug.Log($"[BFOA] Start score={score:F3}");

        return new PathCandidate(p, score);
    }

    // ============================================================
    // TUMBLE
    // ============================================================
    PathCandidate DoTumble(PathCandidate cand, Tile goal)
    {
        var p = new List<Node>(cand.path);
        int L = p.Count;
        if (L < 4) return cand;

        int cutStart = Mathf.FloorToInt(L * 0.7f);
        int cutEnd = Mathf.Max(cutStart, L - 2);
        int cutIndex = UnityEngine.Random.Range(cutStart, cutEnd);

        Debug.Log($"[BFOA]  Tumble cut at index {cutIndex}/{L}");

        p.RemoveRange(cutIndex, p.Count - cutIndex);

        Node last = p[p.Count - 1];

        int choice = UnityEngine.Random.Range(0, 3);
        Debug.Log($"[BFOA]  Tumble decision={choice}");

        if (choice == 0)
        {
            var (a, okA) = Apply(last, RobotAction.TurnLeft);
            if (okA) { p.Add(a); last = a; }
            var (b, okB) = Apply(last, RobotAction.Forward);
            if (okB) { p.Add(b); last = b; }
        }
        else if (choice == 1)
        {
            var (a, okA) = Apply(last, RobotAction.TurnLeft);
            if (okA) { p.Add(a); last = a; }
        }
        else
        {
            var (a, okA) = Apply(last, RobotAction.Forward);
            if (okA) { p.Add(a); last = a; }
        }

        float score = Evaluate(p, (goal.x, goal.y));
        Debug.Log($"[BFOA]  Tumble result score={score:F3}");

        return new PathCandidate(p, score);
    }

    // ============================================================
    // SWIM
    // ============================================================
    PathCandidate DoSwim(PathCandidate cand, Tile goal)
    {
        var p = new List<Node>(cand.path);
        Node last = p[p.Count - 1];

        var (nx, ok) = Apply(last, RobotAction.Forward);

        if (ok)
        {
            p.Add(nx);

            float score = Evaluate(p, (goal.x, goal.y));
            Debug.Log($"[BFOA]    Swim forward -> score={score:F3}");

            return new PathCandidate(p, score);
        }

        Debug.Log("[BFOA]    Swim blocked");
        return cand;
    }

    // ============================================================
    // EVALUATE
    // ============================================================
    float Evaluate(List<Node> p, (int gx, int gy) goal)
    {
        float score = 0f;

        for (int i = 0; i < p.Count - 1; i++)
        {
            Node cur = p[i];
            Node nxt = p[i + 1];

            score += HeuristicDesirability(cur, nxt, goal);
        }

        return score;
    }
}
