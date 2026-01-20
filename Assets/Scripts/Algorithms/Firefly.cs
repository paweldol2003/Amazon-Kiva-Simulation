using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements.Experimental;
using static System.Collections.Specialized.BitVector32;

public partial class PathManager : MonoBehaviour
{
    [Header("Firefly")]
    public int fireflies = 40;
    public int generations = 30;
    public int firesteps = 300;
    [Range(0.1f, 5f)] public float firealpha = 1.0f;     // waga feromonów


    void Firefly_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        Debug.Log($"[Firefly] Starting firefly optimization for robot {robot.Id}");
        float t0 = Time.realtimeSinceStartup;
        int manhattan = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);

        StartCoroutine(Firefly_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;

            if (path == null || path.Count == 0)
            {
                Debug.LogError($"[Firefly] Failed to find ANY path for robot {robot.Id}");
                // Logowanie porażki do CSV
                AlgorithmLogger.LogToCSV("Firefly", elapsed, 0, 0, false, startStep, manhattan);
                return;
            }

            int rotations = 0;
            foreach (var n in path)
                if (n.action == RobotAction.TurnLeft || n.action == RobotAction.TurnRight) rotations++;

            // Logowanie sukcesu do CSV
            AlgorithmLogger.LogToCSV("Firefly", elapsed, path.Count, rotations, true, startStep, manhattan);

            //Debug.Log($"[Firefly] Path found for robot {robot.Id}, length={path.Count}");
            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();
        }));
    }
    IEnumerator Firefly_Coroutine(Tile start, Tile goal, Heading startHead, int startStep,
                               RobotController robot, Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;   // <-- START pomiaru
        int manhattan = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);

        float lastLogTimeMs = 0f;
        float lastLoggedBestFitness = float.NegativeInfinity;


        List<Node> bestGlobalPath = null;
        float bestGlobalFitness = 0f;
        List<Node> finalBestPath = null;
        Tile[,] snapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;


        int width = RTgrid[startStep].GetLength(0);
        int height = RTgrid[startStep].GetLength(1);

        // globalna mapa światła (wykorzystywana przez wszystkie generacje)
        float[,] lightPath = new float[width, height];
        // Tablica list (do reużycia)
        List<Node>[] sharedNexts = new List<Node>[_cachedActions.Length];
        for (int i = 0; i < _cachedActions.Length; i++) sharedNexts[i] = new List<Node>(2); // capacity 2 (bo max forward bonus)

        float[] sharedWeights = new float[_cachedActions.Length];
        int g = 0;
        //for (; g < generations; g++)
        while (Time.realtimeSinceStartup - t0 < 1f) // max 5 sekund
        {
            g++;
            //Debug.Log($"[Firefly] === GENERATION {g} ===");
            RTgrid[startStep] = gm.gridManager.CloneStep(snapshot);

            // 1️⃣ LISTA pathów tej generacji
            List<(List<Node> path, float fitness)> generationPaths = new();
            //bestGenerationPath = null;
            bool improvedThisGen = false;

            // 2️⃣ WYZNACZAMY NOWE ŚCIEŻKI KORZYSTAJĄC ZE STAREGO ŚWIATŁA
            for (int i = 0; i < fireflies; i++)
            {
                List<Node> fireflyPath = FA_SetStartPath(
                start, goal, startHead, startStep, lightPath,
                sharedNexts, sharedWeights
                );
                float fit = Fitness(fireflyPath, goal, start);


                //Debug.Log($"[Firefly] Firefly {i}, pathLen = {fireflyPath.Count}, fitness = {fit:F3}");

                //if (fireflyPath != null)
                //{
                //    foreach (var n in fireflyPath)
                //    {
                //        // Użyj odpowiedniej flagi (BestAlgPath lub BestACOPath jeśli już zmieniłeś enum)
                //        RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;
                //    }
                //    gm.gridManager.RefreshAll(startStep);
                //    yield return null; // Zobaczymy każdą mrówkę z osobna
                //}

                // aktualizacja bestPath
                if (bestGlobalPath == null || fit > Fitness(bestGlobalPath, goal, start))
                {
                    bestGlobalPath = fireflyPath;
                    bestGlobalFitness = 2 * fit;
                    improvedThisGen = true;

                }

                generationPaths.Add((fireflyPath, fit));

            }
            //Debug.Log($"[Firefly] Best global path, pathLen = {bestGlobalPath.Count}, fitness = {bestGlobalFitness:F3}");

            // 3️⃣ WYZERUJ ŚWIATŁO NA KOLEJNĄ GENERACJĘ
            Array.Clear(lightPath, 0, lightPath.Length);

            // 4️⃣ DOPIERO TERAZ PRZYPISUJEMY ŚWIATŁO ZE WSZYSTKICH NOWYCH ŚCIEŻEK
            foreach (var (path, fit) in generationPaths)
                SetLight(lightPath, path, fit);
            SetLight(lightPath, bestGlobalPath, bestGlobalFitness);

            //Debug.Log($"[Firefly] Generation {g} updated light grid.");

            if (bestGlobalPath != null)
            {
                int bestLen = bestGlobalPath.Count;
                float fitness = bestGlobalFitness;

                float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;
                bool timePassed = (elapsedMs - lastLogTimeMs) >= 5f;
                bool betterThanLastLogged = fitness > lastLoggedBestFitness;

                if (improvedThisGen || timePassed)
                {
                    ConvergenceLogger.Log("Firefly", g, elapsedMs, manhattan, fitness, bestLen);
                    lastLogTimeMs = elapsedMs;
                    lastLoggedBestFitness = fitness;
                }
            }

            ////---Czekaj na naciśnięcie klawisza przed przejściem do kolejnej generacji ---
            //Debug.Log($"[Firefly] Press {nextIterationKey} to continue to next generation {g + 1}/{generations}...");

            //// 1️⃣ Czekamy aż użytkownik puści klawisz – aby uniknąć natychmiastowego skipu
            //while (Keyboard.current[nextIterationKey].isPressed)
            //    yield return null;

            //// 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
            //while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
            //    yield return null;


        }
        if (bestGlobalPath[bestGlobalPath.Count - 1].x == goal.x &&
            bestGlobalPath[bestGlobalPath.Count - 1].y == goal.y)
        {
            finalBestPath = bestGlobalPath;
        }

        foreach (var n in finalBestPath)
            RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestFAPath;
        gm.gridManager.RefreshAll(startStep);

        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[Firefly Timer] Firefly Coroutine for robot {robot.Id} took {elapsed:F2} ms and {g} iterations");
        onDone?.Invoke(finalBestPath);
        yield break;
    }

    List<Node> FA_SetStartPath(Tile start, Tile goal, Heading startHead, int startStep, float[,] lightPath,
                                     List<Node>[] nextsBuffer, float[] weightsBuffer)
    {
        List<Node> path = new List<Node>(firesteps);

        Node cur = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        path.Add(cur);

        int stepsTaken = 0;
        int A = _cachedActions.Length;

        while (!(cur.x == goal.x && cur.y == goal.y) && stepsTaken < firesteps)
        {
            for (int i = 0; i < A; i++) nextsBuffer[i].Clear();
            Array.Clear(weightsBuffer, 0, A);

            //// nexts[i] = lista nodów generowanych przez akcję i
            //List<Node>[] nexts = new List<Node>[A];
            //for (int i = 0; i < A; i++)
            //    nexts[i] = new List<Node>();

            //float[] weight = new float[A];
            float sum = 0f;

            // ----------------------------------------
            // GENEROWANIE NASTĘPNIKÓW I LICZENIE WAG
            // ----------------------------------------
            for (int i = 0; i < A; i++)
            {
                var action = _cachedActions[i];

                (Node nxt, bool allowed) = Apply(cur, action);
                if (!allowed)
                {
                    weightsBuffer[i] = 0f;
                    //Debug.Log($"[FA] action={action} → NOT ALLOWED");
                    continue;
                }

                nextsBuffer[i].Add(nxt);

                // --- HEURYSTYKA ---
                float h = FAHeuristicDesirability(cur, nxt, (goal.x, goal.y));
                float L = 0f;

                if (action == RobotAction.Forward) L = lightPath[nxt.x, nxt.y];

                // suma bazowa
                float w = h;
                if (h > 1f) w += L;

                //Debug.Log(
                //    $"[FA] action={action} " +
                //    $"nxt=({nxt.x},{nxt.y}) " +
                //    $"heur={h:F2} light={L:F2} sumBase={w:F2}"
                //);

                // --- BONUS FORWARD ---
                if (action != RobotAction.Forward && action != RobotAction.Wait)
                {
                    (Node nxtfwd, bool okf) = Apply(nxt, RobotAction.Forward);
                    if (okf)
                    {
                        nextsBuffer[i].Add(nxtfwd);

                        float h2 = FAHeuristicDesirability(nxt, nxtfwd, (goal.x, goal.y));
                        float L2 = lightPath[nxtfwd.x, nxtfwd.y];

                        float bonus = h2;
                        if (h2 > 1f) bonus += L2;

                        //Debug.Log(
                        //    $"[FA]          forwardBonus nxt=({nxtfwd.x},{nxtfwd.y}) " +
                        //    $"heur={h2:F2} light={L2:F2} bonus={bonus:F2}"
                        //);

                        w += bonus;
                        w *= 0.10f;
                    }
                }

                weightsBuffer[i] = w;
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
                break;
            }

            // ----------------------------------------
            // WYBÓR AKCJI METODĄ RULETKI
            // ----------------------------------------
            float r = UnityEngine.Random.value * sum;
            float acc = 0f;
            int chosen = -1; // Zmieniono z 0 na -1 dla bezpieczeństwa

            for (int i = 0; i < A; i++)
            {
                // Pomijamy zerowe wagi, żeby nie wybrać pustej listy
                if (weightsBuffer[i] <= 0f) continue;

                acc += weightsBuffer[i];
                if (r <= acc)
                {
                    chosen = i;
                    break;
                }
            }

            // ZABEZPIECZENIE: Jeśli błąd float sprawił, że r > acc, wybieramy ostatnią możliwą akcję
            if (chosen == -1)
            {
                for (int i = A - 1; i >= 0; i--)
                {
                    if (weightsBuffer[i] > 0f)
                    {
                        chosen = i;
                        break;
                    }
                }
            }

            // Jeśli nadal -1 (teoretycznie niemożliwe przy sum > 0), przerywamy
            if (chosen == -1) break;

            var chosenNodes = nextsBuffer[chosen];
            for (int k = 0; k < chosenNodes.Count; k++)
            {
                path.Add(chosenNodes[k]);
                stepsTaken++;
            }

            // Ustawiamy cur na ostatni dodany element
            cur = chosenNodes[chosenNodes.Count - 1];

        }
        //Debug.Log($"[Firefly] Steps taken: {stepsTaken}");
        return path;
    }
    float Fitness(List<Node> path, Tile goal, Tile start)
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
    void SetLight(float[,] lightPath, List<Node> path, float light)
    {
        foreach (var n in path)
        {
            if(n.action == RobotAction.Forward)
                lightPath[n.x,n.y] += (float)light;
        }
    } 
    float FAHeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
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