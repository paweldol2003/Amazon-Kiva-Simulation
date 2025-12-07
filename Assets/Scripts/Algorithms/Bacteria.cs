using GLTFast.Schema;
//using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;
using static System.Collections.Specialized.BitVector32;

public partial class PathManager : MonoBehaviour
{
    [Header("BFOA")]
    public int bacteriaSteps = 300;
    public int bacterias = 40;
    public int chemotaxes = 30;
    public int reproductions = 30;
    public int elimAndDisp = 5;
    [Range(0f, 1f)] public float eliminationProbability = 0.05f;

    public int swimLength = 3;

    //[Range(0.1f, 5f)] public float firealpha = 1.0f;     // waga feromonów


    void BFOA_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        Debug.Log($"[BFOA] Starting BFOA optimization for robot {robot.Id}");

        StartCoroutine(BFOA_Coroutine(start, goal, startHead, startStep, robot, path =>
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
    IEnumerator BFOA_Coroutine(Tile start, Tile goal, Heading startHead, int startStep,
                               RobotController robot, Action<List<Node>> onDone)
    {
        List<Node> bestGlobalPath = null;
        List<Node> finalBestPath = null;
        Tile[,] snapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;


        int width = RTgrid[startStep].GetLength(0);
        int height = RTgrid[startStep].GetLength(1);

        List<Node>[] bacteriaPaths = new List<Node>[bacterias];
        Tile[,] bfoaSnapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;

        for (int b = 0; b < bacterias; b++)
        {
            bacteriaPaths[b] = BFOA_SetPath(start, goal, startHead, startStep);
        }

        for (int ead = 0; ead < elimAndDisp; ead++)
        {
            //Debug.Log($"[Firefly] === GENERATION {g} ===");

            for (int r = 0; r < reproductions; r++)
            {
                RTgrid[startStep] = gm.gridManager.CloneStep(snapshot);

                for (int b = 0; b < bacterias; b++)
                {
                    // 2️⃣ OBLICZAMY FITNESS DLA KAŻDEGO BAKTERII
                    var path = bacteriaPaths[b];
                    int ch = 0;

                    for ( int i = 1; i < path.Count; i++)
                    {
                        Node prevNode = path[i-1];

                        if (ch == chemotaxes)
                            break;  
                        var n = path[i];

                        float chProb = (chemotaxes-ch) / (float)path.Count;

                        if (chProb > UnityEngine.Random.value)
                        {
                            ch++;
                            List<Node> newPath = new List<Node>();

                            //afterTumbleNodes = new List<Node>();
                            (List<Node> afterTumbleNodes, bool pos) = doTumble(prevNode, n, goal);
                            if (pos)
                            {
                                afterTumbleNodes.AddRange(doSwimming(afterTumbleNodes[afterTumbleNodes.Count-1], goal));
                            }
                            newPath.AddRange(afterTumbleNodes);
                            Tile restPathStartTile = RTgrid[afterTumbleNodes[afterTumbleNodes.Count - 1].step][afterTumbleNodes[afterTumbleNodes.Count - 1].x, afterTumbleNodes[afterTumbleNodes.Count - 1].y];
                            newPath.AddRange(BFOA_SetPath(restPathStartTile, goal, afterTumbleNodes[afterTumbleNodes.Count - 1].head, startStep, afterTumbleNodes[afterTumbleNodes.Count - 1]));
                            path.RemoveRange(i-1, path.Count - (i - 1));
                            path.AddRange(newPath);
                        }

                        //RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;
                    }
                    foreach (var n in path)
                        RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;


                    //Debug.Log($"[BFOA] Bacteria {b}, reproduction {r}, health = {fit:F3}");

                }


                Array.Sort(bacteriaPaths, (a, b) => BFOA_Health(b, start, goal).CompareTo(BFOA_Health(a, start, goal)));
                int half = bacterias / 2;

                for (int i = half; i < bacterias; i++)
                {
                    // skopiuj lepszą połowę do gorszej
                    bacteriaPaths[i] = DeepCopy(bacteriaPaths[i - half]);
                }

                //MALOWANIE
                foreach (var n in bacteriaPaths[0])
                    RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
                gm.gridManager.RefreshAll(startStep);

                //---Czekaj na naciśnięcie klawisza przed przejściem do kolejnej generacji ---
                Debug.Log($"[BFOA] Press {nextIterationKey} to continue to next generation {r + 1}/{reproductions}...");

                // 1️⃣ Czekamy aż użytkownik puści klawisz – aby uniknąć natychmiastowego skipu
                while (Keyboard.current[nextIterationKey].isPressed)
                    yield return null;

                // 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
                while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
                    yield return null;



            }

            for (int i = 0; i < bacterias; i++)
            {
                float elimRoll = UnityEngine.Random.value;
                if (elimRoll < eliminationProbability)
                {
                    // eliminujemy i tworzymy nową ścieżkę
                    bacteriaPaths[i] = BFOA_SetPath(start, goal, startHead, startStep);
                }
            }
        }

        for (int b = 0; b < bacterias; b++)
        {
            var path = bacteriaPaths[b];
            float fit = BFOA_Health(path, goal, start);
            if (bestGlobalPath == null || fit > BFOA_Health(bestGlobalPath, goal, start))
            {
                bestGlobalPath = path;
                //Debug.Log($"[BFOA] New best global path found by bacteria {b}, fitness={fit:F3}, length={bestGlobalPath.Count}");
            }
        }

        if (bestGlobalPath[bestGlobalPath.Count - 1].x == goal.x &&
            bestGlobalPath[bestGlobalPath.Count - 1].y == goal.y)
        {
            finalBestPath = bestGlobalPath;
        }
        onDone?.Invoke(finalBestPath);
        yield break;
    }
    List<Node> DeepCopy(List<Node> path)
    {
        List<Node> copy = new List<Node>(path.Count);
        foreach (var node in path)
            copy.Add(new Node(node.x, node.y, node.head, node.action, node.step)); // MUSISZ mieć konstruktor kopiujący

        return copy;
    }


    List<Node> BFOA_SetPath(Tile start, Tile goal, Heading startHead, int startStep, Node? current = null)
    {
        List<Node> path = new List<Node>();
        int stepsTaken = 0;

        if (!current.HasValue)
            current = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        else 
            stepsTaken = current.Value.step - startStep;

        Node cur = current.Value;
        path.Add(cur);


        while (!(cur.x == goal.x && cur.y == goal.y) && stepsTaken < bacteriaSteps)
        {

            List<Node> nexts = PickAction(cur, goal);
            if (nexts == null || nexts.Count == 0)
            {
                // brak możliwych ruchów - kończymy ścieżkę
                Debug.LogError("[BFOA] No possible moves, ending path");
                break;
            }
            // ----------------------------------------
            // DODAJEMY WSZYSTKIE NASTĘPNIKI ZWYBRANEJ AKCJI
            // ----------------------------------------
            foreach (var n in nexts)
            {
                path.Add(n);
                //Debug.Log($"[FA] Chosen action {n.action}");
                stepsTaken++;
            }


            // ustawiamy aktualnego noda na ostatni z listy
            cur = nexts[nexts.Count - 1];

        }
        //Debug.Log($"[Firefly] Steps taken: {stepsTaken}");
        return path;
    }
    List<Node> PickAction(Node cur, Tile goal, RobotAction? actionToRemove = null)
    {
        List<RobotAction> actions = new List<RobotAction>
            {
                RobotAction.Forward,
                RobotAction.TurnLeft,
                RobotAction.TurnRight,
                RobotAction.Wait
            };
        if (actionToRemove.HasValue)
            actions.Remove(actionToRemove.Value);

        int A = actions.Count;

        // nexts[i] = lista nodów generowanych przez akcję i
        List<Node>[] nexts = new List<Node>[A];
        for (int i = 0; i < A; i++)
            nexts[i] = new List<Node>();

        float[] weight = new float[A];
        float sum = 0f;

        // ----------------------------------------
        // GENEROWANIE NASTĘPNIKÓW I LICZENIE WAG
        // ----------------------------------------
        for (int i = 0; i < A; i++)
        {
            var action = actions[i];
            bool allowed;

            (Node nxt, bool ok) = Apply(cur, action);
            allowed = ok;

            if (!allowed)
            {
                weight[i] = 0f;
                //Debug.Log($"[FA] action={action} → NOT ALLOWED");
                continue;
            }

            nexts[i].Add(nxt);

            // --- HEURYSTYKA ---
            float h = BFOA_HeuristicDesirability(cur, nxt, (goal.x, goal.y));


            // suma bazowa
            float w = h;

            //Debug.Log(
            //    $"[FA] action={action} " +
            //    $"nxt=({nxt.x},{nxt.y}) " +
            //    $"heur={h:F2} light={L:F2} sumBase={w:F2}"
            //);

            if (action != RobotAction.Forward && action != RobotAction.Wait)
            {
                (Node nxtfwd, bool okf) = Apply(nxt, RobotAction.Forward);
                if (okf)
                {
                    nexts[i].Add(nxtfwd);

                    float h2 = FAHeuristicDesirability(nxt, nxtfwd, (goal.x, goal.y));

                    float bonus = h2;

                    //Debug.Log(
                    //    $"[FA]          forwardBonus nxt=({nxtfwd.x},{nxtfwd.y}) " +
                    //    $"heur={h2:F2} light={L2:F2} bonus={bonus:F2}"
                    //);

                    w += bonus;
                    w *= 0.10f;
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
            Debug.Log("[Firefly] No possible moves, ending path");
            return null;
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
        return nexts[chosen];
    }

    (List<Node>, bool) doTumble(Node prev, Node cur, Tile goal)
    {
        List<Node> nexts = new List<Node>();
        nexts.Add(prev);
        nexts.AddRange( PickAction(prev, goal, cur.action));
        float improvement = BFOA_HeuristicDesirability(nexts[nexts.Count - 2], nexts[nexts.Count-1], (goal.x, goal.y));
        if (improvement > 5f)
            return (nexts, true);
        return (nexts, false);
    }
    List<Node> doSwimming(Node startNode, Tile goal)
    {
        List<Node> swimPath = new List<Node>();
        Node cur = startNode;
        while (true)
        {             
            (Node nxt, bool ok) = Apply(cur, RobotAction.Forward);
            if (!ok)
                break;
            float improvement = BFOA_HeuristicDesirability(cur, nxt, (goal.x, goal.y));
            if (improvement <= 5f)
                break;
            swimPath.Add(nxt);
            cur = nxt;
        }
        return swimPath;
    }
    float BFOA_Health(List<Node> path, Tile goal, Tile start)
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

    float BFOA_HeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
    {
        float curManhattan = Mathf.Abs(c.x - goal.gx) + Mathf.Abs(c.y - goal.gy);
        float nexManhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);
        float facing = 0.5f;

        int dx = goal.gx - n.x, dy = goal.gy - n.y;
        facing = (n.head == Heading.East && dx > 0) ||
            (n.head == Heading.West && dx < 0) ||
            (n.head == Heading.North && dy > 0) ||
            (n.head == Heading.South && dy < 0) ? 3.5f : 1f;

        //if (Mathf.Abs(dx) > Mathf.Abs(dy))
        //    facing = (n.head == Heading.East && dx > 0) || (n.head == Heading.West && dx < 0) ? 3.5f : 1f;
        //else if (Mathf.Abs(dy) > 0)
        //    facing = (n.head == Heading.North && dy > 0) || (n.head == Heading.South && dy < 0) ? 3.5f : 1f;
        // Im mniejsza odległość do celu, tym większa atrakcyjność
        float baseVal = 30 * (curManhattan - nexManhattan);
        baseVal = Mathf.Max(baseVal, 0f);  // nie pozwalamy na wartości ujemne

        return baseVal + facing;
    }
}