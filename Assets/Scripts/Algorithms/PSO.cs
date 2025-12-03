using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("PSO")]
    public int particles = 3;
    public int maxParticlesSteps = 120;


    // ============================================================
    // ENTRY POINT
    // ============================================================
    void PSO_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        Debug.Log($"[PSO] Starting bacterial optimization for robot {robot.Id}");

        StartCoroutine(PSO_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogError($"[PSO] Failed to find ANY path for robot {robot.Id}");
                return;
            }

            Debug.Log($"[PSO] Path found for robot {robot.Id}, length={path.Count}");

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
    IEnumerator PSO_Coroutine(Tile start, Tile goal, Heading startHead, int startStep,
                                   RobotController robot, Action<List<Node>> onDone)
    {
        List<Node> bestPath = null;
        List<Node> absoluteBestPath = null;
        for (int i = 0; i < particles; i++)
        {
            List<Node> particlePath = SetStartPath(start, goal, startHead, startStep);

            Debug.Log($"[PSO] Ścieżka {i} długości = {particlePath.Count}");

            // liczenie akcji
            int forward = 0, left = 0, right = 0, wait = 0;
            foreach (var n in particlePath)
            {
                switch (n.action)
                {
                    case RobotAction.Forward: forward++; break;
                    case RobotAction.TurnLeft: left++; break;
                    case RobotAction.TurnRight: right++; break;
                    case RobotAction.Wait: wait++; break;
                }
            }

            Debug.Log($"[PSO] {i}: F={forward}, L={left}, R={right}, W={wait}");

            // kolorowanie kafli na czerwono
            foreach (var n in particlePath)
            {
                if (n.step >= 0 && n.step < RTgrid.Count)
                {
                    RTgrid[startStep][n.x, n.y].flags = TileFlags.AlgPath;
                }
            }

            int dx = particlePath[particlePath.Count - 1].x;
            int dy = particlePath[particlePath.Count - 1].y;
            int bx = bestPath != null ? bestPath[bestPath.Count - 1].x : -1;
            int by = bestPath != null ? bestPath[bestPath.Count - 1].y : -1;
            int distNew = Mathf.Abs(dx - goal.x) + Mathf.Abs(dy - goal.y);
            int distBest = bestPath != null
                ? Mathf.Abs(bx - goal.x) + Mathf.Abs(by - goal.y)
                : int.MaxValue;

            if (bestPath == null || distNew < distBest)
            {
                bestPath = particlePath;
            }

        }
        foreach (var n in bestPath)
        {
            if (n.step >= 0 && n.step < RTgrid.Count)
            {
                RTgrid[startStep][n.x, n.y].flags = TileFlags.BestAlgPath;
            }
        }
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;

        Debug.Log($"[PSO] Powinno być czerwone");

        onDone?.Invoke(absoluteBestPath);
        yield break;
    }

    List<Node> SetStartPath(Tile start, Tile goal, Heading startHead, int startStep)
    {
        List<Node> nodes = new List<Node>();

        Node cur = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        nodes.Add(cur);

        for (int step = startStep; step < maxParticlesSteps + startStep; step++)
        {
            if (cur.x == goal.x && cur.y == goal.y)
                break;

            var actions = new List<RobotAction>
            {
                RobotAction.Forward,
                RobotAction.TurnLeft,
                RobotAction.TurnRight,
                RobotAction.Wait
            };

            float sum = 0f;
            float[] weights = new float[actions.Count];
            Node[] nexts = new Node[actions.Count];

            for (int j = 0; j < actions.Count; j++)
            {
                var a = actions[j];
                var (nxt, allowed) = Apply(cur, a);

                if (!allowed)
                {
                    weights[j] = 0f;
                    continue;
                }

                float eta = PSOHeuristicDesirability(cur, nxt, (goal.x, goal.y));
                eta = Mathf.Pow(eta, 1f);

                float w = Mathf.Max(eta, 1e-6f);
                weights[j] = w;
                nexts[j] = nxt;
                sum += w;
            }

            if (sum <= 0f)
            {
                for (int j = 0; j < actions.Count; j++)
                {
                    var (nxt, allowed) = Apply(cur, actions[j]);
                    if (allowed)
                    {
                        cur = nxt;
                        nodes.Add(cur);
                        continue;
                    }
                }
                break;
            }

            float r = UnityEngine.Random.value * sum;
            float acc = 0f;
            int chosenIndex = 0;

            for (int j = 0; j < actions.Count; j++)
            {
                acc += weights[j];
                if (r <= acc)
                {
                    chosenIndex = j;
                    break;
                }
            }
            // DEBUG WAG
            Debug.Log(
                $"[PSO] step={step} | " +
                $"F={weights[0]:F3}  " +
                $"L={weights[1]:F3}  " +
                $"R={weights[2]:F3}  " +
                $"W={weights[3]:F3}  " +
                $"sum={sum:F3}"
            );
            Debug.Log($"[PSO] step={step} | chosen={actions[chosenIndex]}");



            cur = nexts[chosenIndex];
            nodes.Add(cur);

        }

        return nodes;
    }

    float PSOHeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
    {
        // im bliżej celu tym lepiej, delikatna premia za „patrzenie” w kierunku celu
        float curManhattan = Mathf.Abs(c.x - goal.gx) + Mathf.Abs(c.y - goal.gy);
        float nexManhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);
        float facing = 1f;

        int dx = goal.gx - n.x, dy = goal.gy - n.y;
        if (Mathf.Abs(dx) > Mathf.Abs(dy))
            facing = (n.head == Heading.East && dx > 0) || (n.head == Heading.West && dx < 0) ? 3.5f : 1f;
        else if (Mathf.Abs(dy) > 0)
            facing = (n.head == Heading.North && dy > 0) || (n.head == Heading.South && dy < 0) ? 3.5f : 1f;
        return 20 * (curManhattan - nexManhattan) + facing + 2 * Mathf.Abs(c.head - n.head); /// ((nexManhattan + 2f)*2);
    }
}