using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using UnityEngine.InputSystem;

using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("Camel")]
    public int herds = 5;
    public int camels = 20;
    public int camelsteps = 32;

    void Camel_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        //System.Action<List<Node>> onDone;
        StartCoroutine(Camel_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogError($"No path found for robot {robot.Id} to point.");
                return;
            }

            //for (int i = 0; i < path.Count - 1; i++)
            //{
            //    var node = path[i];
            //    var nxtnode = path[i + 1];

            //    int step = node.step;
            //    if (step < 0 || step >= RTgrid.Count) continue;

            //    var gridStep = RTgrid[step];

            //    gridStep[node.x, node.y].flags |= TileFlags.Blocked;
            //    gridStep[nxtnode.x, nxtnode.y].flags |= TileFlags.Blocked;

            //    gm.gridManager.UpdateRTgrid(step, gridStep);
            //}

            Debug.LogWarning($"Assigning point path to robot {robot.Id}, path size: {path.Count}");
            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();

        }));
    }
    IEnumerator Camel_Coroutine(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;   // <-- START pomiaru

        Tile[,] snapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;


        List<Node> bestPath = null;
        float bestPathHumidity = float.NegativeInfinity;

        Debug.LogWarning($"[Camel] Starting pathfinding from ({start.x},{start.y}) to ({goal.x},{goal.y})");
        for (int h = 0; h < herds; h++)
        {
            List<Node> herdPath = new List<Node>();
            herdPath.Add(new Node(start.x, start.y, startHead, RobotAction.Wait, startStep));
            while (herdPath[herdPath.Count - 1].x != goal.x || herdPath[herdPath.Count - 1].y != goal.y)
            {
                RTgrid[startStep] = gm.gridManager.CloneStep(snapshot);

                Node lastHerdNode = herdPath[herdPath.Count - 1];
                int herdStartStep = lastHerdNode.step;
                Tile herdStart = RTgrid[herdStartStep][lastHerdNode.x, lastHerdNode.y];
                Dictionary<Node, float> humiditySum = new Dictionary<Node, float>();
                Node bestNextNode = default;
                float bestHumidity = float.NegativeInfinity;

                for (int c = 0; c < camels; c++)
                {
                    List<Node> camelPath = Camel_SetPath(herdStart, goal, lastHerdNode.head, startStep, lastHerdNode);

                    float humidity = Camel_Humidity(camelPath, start, goal);
                    if (camelPath.Count == 0)
                    {
                        Debug.LogError($"[Camel] Camel {c} produced empty path, skipping");
                        Debug.LogError($"[Camel] Last herd node: ({lastHerdNode.x},{lastHerdNode.y},{lastHerdNode.action}) step {lastHerdNode.step}");
                        continue;
                    }

                    Node camelNext = camelPath[0];
                    if (humidity > bestHumidity)
                    {
                        bestNextNode = camelNext;
                        bestHumidity = humidity;
                    }

                    //if (!humiditySum.ContainsKey(camelNext))
                    //    humiditySum[camelNext] = 0f;

                    //humiditySum[camelNext] += humidity;
                    foreach (var n in camelPath)
                        RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;
                }



                //foreach (var kv in humiditySum)
                //{
                //    if (kv.Value > bestHumidity)
                //    {
                //        bestHumidity = kv.Value;
                //        bestNextNode = kv.Key;
                //    }
                //}
                herdPath.Add(bestNextNode);
                //Debug.LogWarning($"[Camel] Herd {h}, added node ({bestNextNode.x},{bestNextNode.y},{bestNextNode.action}) with humidity {bestHumidity:F2}");
                foreach (var n in herdPath)
                    RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
                gm.gridManager.RefreshAll(startStep);
                //while (Keyboard.current[nextIterationKey].isPressed)
                //    yield return null;

                //// 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
                //while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
                //    yield return null;
            }
            float herdPathHumidity = Camel_Humidity(herdPath, start, goal);
            if (bestPathHumidity < herdPathHumidity)
            {
                bestPathHumidity = herdPathHumidity;
                bestPath = herdPath;
            }
        }
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[Camel Timer] Camel Coroutine for robot {robot.Id} took {elapsed:F2} ms");


        onDone?.Invoke(bestPath);

        yield return null;
    }

    List<Node> Camel_SetPath(Tile start, Tile goal, Heading startHead, int startStep, Node cur)
    {
        List<Node> path = new List<Node>();
        //int stepsTaken = cur.step-startStep;
        int stepsTaken = 0;
        //Debug.Log($"[Camel] Steps taken so far: {stepsTaken}");

        while (!(cur.x == goal.x && cur.y == goal.y) && stepsTaken < camelsteps)
        {
            List<RobotAction> actions = new List<RobotAction>
            {
                RobotAction.Forward,
                RobotAction.TurnLeft,
                RobotAction.TurnRight,
                RobotAction.Wait
            };

            int A = actions.Count;

            // nexts[i] = lista nodów generowanych przez akcję i
            List<Node>[] nexts = new List<Node>[A];
            for (int i = 0; i < A; i++)
                nexts[i] = new List<Node>();

            float[] weight = new float[A];
            float sum = 0f;
            for (int i = 0; i < A; i++)
            {
                var action = actions[i];
                bool allowed;

                (Node nxt, bool ok) = Apply(cur, action);
                allowed = ok;

                if (!allowed)
                {
                    weight[i] = 0f;
                    //Debug.LogWarning($"[FA] action={action} → NOT ALLOWED");
                    continue;
                }

                nexts[i].Add(nxt);

                // --- HEURYSTYKA ---
                float h = Camel_HeuristicDesirability(cur, nxt, (goal.x, goal.y));

                // suma bazowa
                float w = h;

                //Debug.Log(
                //    $"[FA] action={action} " +
                //    $"nxt=({nxt.x},{nxt.y}) " +
                //    $"heur={h:F2} light={L:F2} sumBase={w:F2}"
                //);

                // --- FORWARD ---
                if (action != RobotAction.Forward/* && action != RobotAction.Wait*/)
                {
                    (Node nxtfwd, bool okf) = Apply(nxt, RobotAction.Forward);
                    if (okf)
                    {
                        nexts[i].Add(nxtfwd);

                        float h2 = Camel_HeuristicDesirability(nxt, nxtfwd, (goal.x, goal.y));


                        //Debug.Log(
                        //    $"[FA]          forwardBonus nxt=({nxtfwd.x},{nxtfwd.y}) " +
                        //    $"heur={h2:F2} light={L2:F2} bonus={bonus:F2}"
                        //);

                        w += h2;
                        w *= 0.20f;
                    }
                    else if (action == RobotAction.Wait)
                    {
                        w= 0f;
                    }
                }

                weight[i] = w;
                sum += w;

                //Debug.Log(
                //    $"[FA] FINAL action={action} weight={w:F2} (accumulated sum={sum:F2})"
                //);
            }
            // --- TABELA WAG PO AKCJACH ---
            //Debug.Log(
            //    $"[FA] Weights → F={weight[0]:F2}, L={weight[1]:F2}, R={weight[2]:F2}, W={weight[3]:F2}, sum={sum:F2}"
            //);

            if (sum <= 0f)
            {
                // brak możliwości ruchu -> kończymy ścieżkę
                Debug.Log("[Camel] No possible moves, ending path");
                break;
            }

            // ----------------------------------------
            // WYBÓR AKCJI METODĄ RULETKI
            // ----------------------------------------
            float r = UnityEngine.Random.value * sum;
            float acc = 0f;
            int chosen = 0;

            for (int i = 0; i < A; i++)
            {
                acc += weight[i];
                if (r <= acc)
                {
                    chosen = i;
                    break;
                }
            }

            // ----------------------------------------
            // DODAJEMY WSZYSTKIE NASTĘPNIKI ZWYBRANEJ AKCJI
            // ----------------------------------------
            foreach (var n in nexts[chosen])
            {
                path.Add(n);
                //Debug.Log($"[FA] Chosen action {n.action}");
                stepsTaken++;
            }
            // ustawiamy aktualnego noda na ostatni z listy
            cur = path[path.Count-1];
        }

        return path;
    }

    float Camel_Humidity(List<Node> path, Tile start, Tile goal)
    {
        if (path == null || path.Count == 0) return 0f;

        Node last = path[path.Count - 1];
        float wholeDist = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
        float dist = Mathf.Abs(last.x - goal.x) + Mathf.Abs(last.y - goal.y);
        float percent = 1f - (dist / wholeDist);

        if (dist == 0)
            return (30f * wholeDist / path.Count) /*+ 10f * percent*/;
        return 10f * percent;
    }
    float Camel_HeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
    {
        float curManhattan = Mathf.Abs(c.x - goal.gx) + Mathf.Abs(c.y - goal.gy);
        float nexManhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);
        float facing = 0.5f;

        int dx = goal.gx - n.x, dy = goal.gy - n.y;
        facing = (n.head == Heading.East && dx > 0) ||
            (n.head == Heading.West && dx < 0) ||
            (n.head == Heading.North && dy > 0) ||
            (n.head == Heading.South && dy < 0) ? 2.5f : 1f;

        float baseVal = 50 * (curManhattan - nexManhattan);
        baseVal = Mathf.Max(baseVal, 0f);  // nie pozwalamy na wartości ujemne

        return baseVal + facing;
    }
}
