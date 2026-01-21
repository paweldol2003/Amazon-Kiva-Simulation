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
    public int camelStepsToAssign = 3;

    void Camel_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        float t0 = Time.realtimeSinceStartup;
        int manhattan = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);

        StartCoroutine(Camel_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;

            if (path == null || path.Count == 0)
            {
                Debug.LogError($"No path found for robot {robot.Id} to point.");
                // Logowanie porażki do CSV
                AlgorithmLogger.LogToCSV("Camel", elapsed, 0, 0, false, startStep, manhattan);
                return;
            }

            int rotations = 0;
            foreach (var n in path)
                if (n.action == RobotAction.TurnLeft || n.action == RobotAction.TurnRight) rotations++;

            // Logowanie sukcesu do CSV
            AlgorithmLogger.LogToCSV("Camel", elapsed, path.Count, rotations, true, startStep, manhattan);

            //Debug.LogWarning($"Assigning point path to robot {robot.Id}, path size: {path.Count}");
            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();
        }));
    }
    IEnumerator Camel_Coroutine(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;   // <-- START pomiaru
        int manhattan = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);

        float lastLogTimeMs = 0f;
        float lastLoggedBestFitness = float.NegativeInfinity;

        Tile[,] snapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;


        List<Node> bestPath = null;
        float bestPathHumidity = float.NegativeInfinity;

        Debug.LogWarning($"[Camel] Starting pathfinding from ({start.x},{start.y}) to ({goal.x},{goal.y})");
        //for (int h = 0; h < herds; h++)
        int h = 0;
        while (Time.realtimeSinceStartup - t0 < 1f) // max 
        {
            h++;
            List<Node> herdPath = new List<Node>();
            herdPath.Add(new Node(start.x, start.y, startHead, RobotAction.Wait, startStep));
            RTgrid[startStep] = gm.gridManager.CloneStep(snapshot);
            int safetyCounter = 0;
            while (herdPath[herdPath.Count - 1].x != goal.x || herdPath[herdPath.Count - 1].y != goal.y)
            {
                if (safetyCounter++ > 1000)
                {
                    Debug.LogError("Safety break: Infinite loop detected in herd pathfinding.");
                    break;
                }
                Node lastHerdNode = herdPath[herdPath.Count - 1];
                int herdStartStep = lastHerdNode.step;
                Tile herdStart = RTgrid[herdStartStep][lastHerdNode.x, lastHerdNode.y];
                Dictionary<Node, float> humiditySum = new Dictionary<Node, float>();

                float bestHumidity = float.NegativeInfinity;

                List<Node> bestSegment = new List<Node>();
                bestSegment.Clear(); // Czyścimy poprzedni "najlepszy" wynik

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

                    if (humidity > bestHumidity)
                    {
                        bestHumidity = humidity;
                        bestSegment.Clear(); // Czyścimy TYLKO gdy znaleźliśmy lepszego kandydata

                        // Dodajemy nowy najlepszy segment
                        int segmentLength = Mathf.Min(camelPath.Count, camelStepsToAssign);
                        for (int i = 0; i < segmentLength; i++)
                            bestSegment.Add(camelPath[i]);
                    }
                    //foreach (var n in camelPath)
                    //    RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;
                    //gm.gridManager.RefreshAll(startStep);

                }
                if (bestSegment.Count == 0)
                {
                    Debug.LogError($"[Camel] Stuck! No camel found a valid path from ({lastHerdNode.x},{lastHerdNode.y}). Breaking loop.");
                    break; // Przerywamy pętlę, żeby nie zawiesić Unity
                }
                herdPath.AddRange(bestSegment);
                //Debug.LogWarning($"[Camel] Herd {h}, added node ({bestNextNode.x},{bestNextNode.y},{bestNextNode.action}) with humidity {bestHumidity:F2}");
                //foreach (var n in herdPath)
                //    RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
                //gm.gridManager.RefreshAll(startStep);
                //while (Keyboard.current[nextIterationKey].isPressed)
                //    yield return null;

                //// 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
                //while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
                //    yield return null;
            }
            float herdPathHumidity = Camel_Humidity(herdPath, start, goal);
            bool improved = false;

            if (bestPathHumidity < herdPathHumidity)
            {
                bestPathHumidity = herdPathHumidity;
                bestPath = herdPath;
                improved = true;
            }

            // --- LOG ZBIEŻNOŚCI ---
            if (bestPath != null)
            {
                int bestLen = bestPath.Count;
                float fitness = bestPathHumidity; // u Ciebie większe = lepsze

                float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;
                bool timePassed = (elapsedMs - lastLogTimeMs) >= 5f;
                bool betterThanLastLogged = fitness > lastLoggedBestFitness;

                if (improved || timePassed)
                {
                    ConvergenceLogger.Log("Camel", h, elapsedMs, manhattan, fitness, bestLen);
                    lastLogTimeMs = elapsedMs;
                    lastLoggedBestFitness = fitness;
                }
            }

        }
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[Camel Timer] Camel Coroutine for robot {robot.Id} took {elapsed:F2} ms and {h} iterations");
        foreach (var n in bestPath)
            RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestCHAPath;
        gm.gridManager.RefreshAll(startStep);

        onDone?.Invoke(bestPath);

        yield return null;
    }    

    // Pola klasy do reużywania (Cache), żeby nie robić 'new List'
    private List<Node> _reusablePathList = new List<Node>(64);
    private List<Node>[] _reusableNexts = new List<Node>[]{ new List<Node>(4), new List<Node>(4), new List<Node>(4), new List<Node>(4)};
    List<Node> Camel_SetPath(Tile start, Tile goal, Heading startHead, int startStep, Node cur)
    {
        // Zamiast new List<Node>(), używamy nowej, ale lokalnie (jeśli musisz zwracać nową instancję)
        // LUB lepiej: przekazuj listę do wypełnienia jako argument.
        // Tutaj założę wersję prostszą:
        List<Node> path = new List<Node>(camelsteps); // Z góry alokuj pojemność!

        int stepsTaken = 0;

        // Cache'owanie celów, żeby nie czytać z właściwości (mikro-optymalizacja)
        int gx = goal.x;
        int gy = goal.y;

        while (!(cur.x == gx && cur.y == gy) && stepsTaken < camelsteps)
        {
            // Wyczyszczenie reużywalnych list
            for (int i = 0; i < 4; i++) _reusableNexts[i].Clear();

            float[] weight = new float[4]; // To jest małe (stack), ujdzie, ale można też scache'ować
            float sum = 0f;

            for (int i = 0; i < 4; i++)
            {
                var action = _cachedActions[i];

                // Apply musi być szybkie i nie alokować!
                (Node nxt, bool ok) = Apply(cur, action);

                if (!ok) continue;

                _reusableNexts[i].Add(nxt);

                // Inlining heurystyki (lub upewnienie się, że jest szybka)
                float h = Camel_HeuristicDesirability(cur, nxt, (gx, gy));
                float w = h;

                if (action != RobotAction.Forward)
                {
                    (Node nxtfwd, bool okf) = Apply(nxt, RobotAction.Forward);
                    if (okf)
                    {
                        _reusableNexts[i].Add(nxtfwd);
                        float h2 = Camel_HeuristicDesirability(nxt, nxtfwd, (gx, gy));
                        w += h2;
                        w *= 0.20f;
                    }
                    else if (action == RobotAction.Wait) w = 0f;
                }

                weight[i] = w;
                sum += w;
            }

            if (sum <= 0f) break;

            // Ruletka
            float r = UnityEngine.Random.value * sum;
            float acc = 0f;
            int chosen = 0;

            for (int i = 0; i < 4; i++)
            {
                acc += weight[i];
                if (r <= acc)
                {
                    chosen = i;
                    break;
                }
            }

            // Dodawanie do ścieżki
            var chosenList = _reusableNexts[chosen];
            int count = chosenList.Count;
            for (int k = 0; k < count; k++)
            {
                var n = chosenList[k];
                path.Add(n);
                stepsTaken++;
            }

            if (path.Count > 0)
                cur = path[path.Count - 1];
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
