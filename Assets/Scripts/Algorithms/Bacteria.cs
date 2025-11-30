using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("Bacteria")]
    public int bacterias = 40;
    public int maxSwimmingSteps = 6;
    public int maxChemotaxisSteps = 40;
    public int reproductioniterations = 60;
   // [Range(0.1f, 5f)] public float firealpha = 1.0f;     // waga feromonów


    void Bacteria_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        StartCoroutine(Bacteria_Coroutine(start, goal, startHead, startStep, robot, path =>
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

    IEnumerator Bacteria_Coroutine( Tile start,
                                    Tile goal,
                                    Heading startHead,
                                    int startStep,
                                    RobotController robot,
                                    Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;

        int w = RTgrid[startStep].GetLength(0);
        int h = RTgrid[startStep].GetLength(1);

        // ======== Parametry BFOA ========
        int population = bacterias;
        int Nc = maxChemotaxisSteps;      // liczba kroków chemotaksji
        int Ns = maxSwimmingSteps;        // maksymalna d³ugoœæ swim
        int Nre = reproductioniterations; // ile cykli reprodukcji
        float Ped = 0.2f;                 // szansa dyspersji (teleportu)
        int Ned = 4;                      // cykle dyspersji

        List<PathCandidate> bacteria = new List<PathCandidate>(population);

        // ======== Inicjalizacja populacji losowych tras ========
        for (int i = 0; i < population; i++)
            bacteria.Add(RandomPath(start, goal, startHead, startStep, w, h));

        PathCandidate bestGlobal = null;

        // ======== DYSPERSJA CYKLAMI ========
        for (int ed = 0; ed < Ned; ed++)
        {
            // ======== REPRODUKCJA CYKLAMI ========
            for (int re = 0; re < Nre; re++)
            {
                // ======== CHEMOTAKSJA ========
                for (int i = 0; i < population; i++)
                {
                    PathCandidate current = bacteria[i];
                    float lastCost = current.cost;

                    // chemotaksja: tumble + swim
                    for (int c = 0; c < Nc; c++)
                    {
                        var tumble = Mutate(current, (goal.x, goal.y));

                        if (tumble.cost < lastCost)
                        {
                            current = tumble;
                            lastCost = tumble.cost;

                            // swim
                            for (int s = 0; s < Ns; s++)
                            {
                                var swim = Mutate(current, (goal.x, goal.y));
                                if (swim.cost < lastCost)
                                {
                                    current = swim;
                                    lastCost = swim.cost;
                                }
                                else break;
                            }
                        }
                    }


                    bacteria[i] = current;
                    if (bestGlobal == null || current.cost < bestGlobal.cost)
                        bestGlobal = current;
                }

                // ======== REPRODUKCJA ========
                bacteria.Sort((a, b) => a.cost.CompareTo(b.cost));
                int half = population / 2;
                for (int i = half; i < population; i++)
                    bacteria[i] = bacteria[i - half].Clone();
            }

            // ======== DYSPERSJA ========
            for (int i = 0; i < population; i++)
            {
                if (UnityEngine.Random.value < Ped)
                    bacteria[i] = RandomPath(start, goal, startHead, startStep, w, h);
            }
        }

        // ======== ZWROT ŒCIE¯KI ========
        onDone?.Invoke(bestGlobal?.path);
        float t1 = Time.realtimeSinceStartup;

        Debug.LogWarning($"[BFOA] Robot {robot.Id} took {(t1 - t0) * 1000f:F1} ms (len={bestGlobal?.path.Count})");

        yield return null;
    }
    class PathCandidate
    {
        public List<PathManager.Node> path;
        public float cost;

        public PathCandidate(List<PathManager.Node> p, float c)
        {
            path = p;
            cost = c;
        }

        public PathCandidate Clone()
            => new PathCandidate(new List<PathManager.Node>(path), cost);
    }
    PathCandidate RandomPath(Tile start,Tile goal,Heading startHead,int startStep,int w,int h)
    {
        var list = new List<PathManager.Node>();
        var node = new PathManager.Node(start.x, start.y, startHead, PathManager.RobotAction.Wait, startStep);
        list.Add(node);

        for (int s = 0; s < 100; s++)
        {
            int dir = UnityEngine.Random.Range(0, 4);
            PathManager.RobotAction a = (PathManager.RobotAction)dir;

            var (next, ok) = Apply(node, a, w, h);
            if (!ok) continue;

            list.Add(next);
            node = next;

            if (node.x == goal.x && node.y == goal.y)
                break;
        }

        return new PathCandidate(list, Evaluate(list, (goal.x, goal.y)));
    }
    
    PathCandidate Mutate(PathCandidate cand, (int gx, int gy) goal)
    {
        var p = new List<PathManager.Node>(cand.path);
        if (p.Count <= 2)
            return cand;

        int cut = UnityEngine.Random.Range(1, p.Count - 1);
        p.RemoveRange(cut, p.Count - cut);

        var last = p[p.Count - 1];

        for (int j = 0; j < 8; j++)
        {
            int dir = UnityEngine.Random.Range(0, 4);
            var a = (PathManager.RobotAction)dir;

            var (nx, ok) = Apply(
                last,
                a,
                RTgrid[last.step + 1].GetLength(0),
                RTgrid[last.step + 1].GetLength(1));

            if (!ok) continue;

            p.Add(nx);
            last = nx;
        }

        return new PathCandidate(p, Evaluate(p, goal));
    }

    float Evaluate(List<PathManager.Node> p, (int gx, int gy) goal)
    {
        float cost = 0f;

        for (int i = 0; i < p.Count; i++)
        {
            var n = p[i];

            // kara za blokadê
            if (!RTgrid[n.step][n.x, n.y].Walkable)
                cost += 5000f;

            // dystans do celu
            cost += Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);

            // kara za d³ugoœæ kroku
            cost += 0.5f;
        }

        return cost;
    }
}