using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;
using System.Text;

public partial class PathManager : MonoBehaviour
{
    // --- Parametry Algorytmu Genetycznego (GA) ---
    [Header("Genetic Algorithm 2")]
    public int GA2populationSize = 100;
    public int GA2Iterations = 100;
    [Range(0.01f, 0.9f)] public float GA2crossoverRate = 0.8f; // Prawdopodobieństwo krzyżowania
    [Range(0.001f, 0.5f)] public float GA2mutationRate = 0.05f; // Prawdopodobieństwo mutacji
    public int GA2tournamentSize = 5; // Parametr dla selekcji turniejowej
    public int GA2maxSteps = 300; // Maksymalna długość ścieżki/chromosomu
    public int GA2fitnessPenalty = 100; // Kara za niechodliwe pola
    public int GA2elitismCount =5; // Liczba elitarnych chromosomów do zachowania
    string csvPath;

    void GA2_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        StartCoroutine(GA2_Coroutine(start, goal, startHead, startStep, robot, path =>
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
    IEnumerator GA2_Coroutine(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;
        RTgrid[startStep][goal.x, goal.y].flags = TileFlags.Goal;

        Tile[,] snapshot = gm.gridManager.CloneStep(RTgrid[startStep]);
        InitCsv();

        // 1. Inicjalizacja
        (List<RobotAction> actions, List<Node> nodes, float fitness)[] population = new (List<RobotAction>, List<Node>, float)[populationSize];
        for (int i = 0; i <populationSize; i++)
        {
            //population[i] = GA2_SetStartPath(start, goal, startHead, startStep);
            population[i] = GA2_SetStartPath(start, goal, startHead, startStep);
        }
        (List<RobotAction> finalActions, List<Node> finalNodes, float finalFitness) bestChromosome = (null, null, float.MinValue);


        for (int it = 0; it < GA2Iterations; it++)
        {
            RTgrid[startStep] = gm.gridManager.CloneStep(snapshot);

            float tIter = Time.realtimeSinceStartup;

            // --- KROK 1: SORTOWANIE I BEST ---
            // Sortujemy malejąco po Fitness (Item3)
            System.Array.Sort(population, (a, b) => b.fitness.CompareTo(a.fitness));

            // Aktualizacja najlepszego w historii (jeśli obecny lider jest lepszy)
            if (bestChromosome.finalActions == null || population[0].fitness > bestChromosome.finalFitness)
            {
                bestChromosome = population[0];
                Debug.LogWarning($"[GA] New best fitness: {bestChromosome.finalFitness:F2}, count: {bestChromosome.finalNodes.Count} at iteration {it}");
                AppendCsv(it, bestChromosome.finalFitness, bestChromosome.finalNodes.Count);
            }
            // --- KROK 2: NOWA POPULACJA ---
            var newPopulation = new (List<RobotAction>, List<Node>, float)[GA2populationSize];
            int newPopIndex = 0;

            // A. ELITYZM: Przepisz rodziców bez zmian
            for (; newPopIndex < GA2elitismCount; newPopIndex++)
            {
                newPopulation[newPopIndex] = population[newPopIndex];
            }
            // B. HODOWLA RESZTY
            while (newPopIndex < GA2populationSize)
            {
                // 1. Selekcja
                var parentA = GA2_TournamentSelection(population);
                var parentB = GA2_TournamentSelection(population);

                // 2. Krzyżowanie (operujemy na List<RobotAction> - Item1)
                List<RobotAction> childActions1;
                List<RobotAction> childActions2;
                if (UnityEngine.Random.value < GA2crossoverRate)
                {
                    childActions1 = GA2_Crossover(parentA.Item1, parentB.Item1);
                    childActions2 = GA2_Crossover(parentB.Item1, parentA.Item1);
                }
                else
                {
                    childActions1 = new List<RobotAction>(parentA.Item1); // Kopia rodzica A
                    childActions2 = new List<RobotAction>(parentB.Item1); // Kopia rodzica B
                }

                // 3. Mutacja
                GA2_Mutate(childActions1);
                GA2_Mutate(childActions2);

                // 4. Ewaluacja (Symulacja fenotypu z genotypu)
                // To jest kluczowe: zamieniamy zmienione akcje na nową ścieżkę i fitness
                var childPhenotype1 = GA2_SimulatePath(childActions1, start, goal, startHead, startStep);
                var childPhenotype2 = GA2_SimulatePath(childActions2, start, goal, startHead, startStep);


                newPopulation[newPopIndex++] = childPhenotype1;
                if (newPopIndex < GA2populationSize)
                {
                    newPopulation[newPopIndex++] = childPhenotype2;
                }
            }

            // Podmiana populacji
            population = newPopulation;

            //WYBÓR RODZICÓW
            //KZYŻOWANIE
            //MUTACJA

            //population = newPopulation;
            //Debug.Log($"[GA] Iter {it} took {(Time.realtimeSinceStartup - tIter) * 1000f} ms. Best Fitness: {bestChromosome.finalFitness:F2}");
            //Debug.Log($"[GA] Best path length: {bestChromosome.finalActions.Count}");
            //for (int a = 0; a < bestChromosome.finalActions.Count; a += 5)
            //{
            //    Debug.Log($"    Step {a}: {bestChromosome.finalActions[a]}");
            //}
            foreach (var ch in population)
                foreach (var n in ch.nodes)
                    RTgrid[startStep][n.x, n.y].flags |= TileFlags.AlgPath;
            foreach (var n in bestChromosome.finalNodes)
                RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
            gm.gridManager.RefreshAll(startStep);

            //while (Keyboard.current[nextIterationKey].isPressed)
            //    yield return null;

            //// 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
            //while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
            //    yield return null;

            yield return null; // Odczekaj jedną klatkę
        }


        // Zakończenie
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[GA Timer] GA for robot {robot.Id} took {elapsed:F2} ms");
        if (bestChromosome.finalNodes[bestChromosome.finalNodes.Count-1].x == goal.x &&
            bestChromosome.finalNodes[bestChromosome.finalNodes.Count - 1].y == goal.y)
        {
            onDone?.Invoke(bestChromosome.finalNodes);
        }
    }

    (List<RobotAction>, List<Node>, float) GA2_SetStartPath(Tile start, Tile goal, Heading startHead, int startStep)
    {
        List<Node> startPath = new List<Node>();
        List<RobotAction> startActions = new List<RobotAction>();

        Node cur = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        RobotAction curAction = RobotAction.Wait;

        startPath.Add(cur);
        startActions.Add(curAction);
        int stepsTaken = 0;

        while (!(cur.x == goal.x && cur.y == goal.y) && stepsTaken < GA2maxSteps)
        {
            List<RobotAction> actions = new List<RobotAction>
            {
                RobotAction.Forward,
                RobotAction.TurnLeft,
                RobotAction.TurnRight,
                RobotAction.Wait
            };

            // Zamiast usuwać z listy po której iterujemy:
            List<RobotAction> validActions = new List<RobotAction>();

            foreach (var action in actions)
            {
                (Node nxt, bool allowed) = Apply(cur, action);
                if (allowed) validActions.Add(action);
            }

            // Jeśli robot utknął (brak ruchów), przerywamy tworzenie tej ścieżki
            if (validActions.Count == 0) break;

            int result = UnityEngine.Random.Range(0, validActions.Count);
            RobotAction chosenAction = validActions[result];

            (Node chosenNode, bool ok) = Apply(cur, chosenAction);

            startPath.Add(chosenNode);
            startActions.Add(chosenAction);
            cur = chosenNode; // WAŻNE: Musisz zaktualizować 'cur' na nową pozycję!
            stepsTaken++;
        }
        float fitness = GA2_Fitness(startPath, goal, start);
        return (startActions, startPath, fitness);
    }
    //float GA2_Fitness(List<Node> path, Tile goal, Tile start)
    //{
    //    if (path == null || path.Count == 0) return 0f;

    //    Node last = path[path.Count - 1];
    //    float wholeDist = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
    //    float dist = Mathf.Abs(last.x - goal.x) + Mathf.Abs(last.y - goal.y);
    //    float percent = 1f - (dist / wholeDist);
    //    float fitness = 0f;
    //    foreach (var n in path)
    //    {
    //        if (!RTgrid[n.step][n.x, n.y].Walkable)
    //        {
    //            fitness -= GA2fitnessPenalty; // Duża kara za niechodliwe pola
    //        }
    //        if (n.action == RobotAction.Wait)
    //        {
    //            fitness -= 50f; // Mała kara za stanie w miejscu
    //        }
    //        else if (n.action == RobotAction.TurnLeft || n.action == RobotAction.TurnRight)
    //        {
    //            fitness -= 10f; // Mała kara za obrót
    //        }
    //    }
    //    if (dist == 0) fitness += 2000f;
    //    else fitness += 100f * percent;
    //    return fitness;

    //}

    void InitCsv()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        csvPath = Path.Combine(
            Application.persistentDataPath,
            $"ga_log_{timestamp}.csv"
        );

        Debug.LogWarning($"CSV path: {csvPath}");
        File.WriteAllText(csvPath, "iteration,fitness,node_count\n");
    }
    void AppendCsv(int iteration, float fitness, int nodeCount)
    {
        string line = $"{iteration};{fitness:F4};{nodeCount}\n";
        File.AppendAllText(csvPath, line);
    }

    float GA2_Fitness(List<Node> path, Tile goal, Tile start)
{
    if (path == null || path.Count == 0) return 0f;

    Node last = path[path.Count - 1];
    float dist = Mathf.Abs(last.x - goal.x) + Mathf.Abs(last.y - goal.y);
    float startDist = Mathf.Abs(start.x - goal.x) + Mathf.Abs(start.y - goal.y);
    
    // Zabezpieczenie przed dzieleniem przez 0
    if (startDist < 1f) startDist = 1f; 

    float fitness = 0f;
    bool reachedGoal = (dist == 0);

    // --- PRIORYTET 1: CZY DOTARŁ? ---
    if (reachedGoal)
    {
        // Ogromna nagroda bazowa (np. 10,000)
        // OD TEGO odejmujemy liczbę kroków.
        // Dzięki temu trasa 50 kroków da (10000 - 500) = 9500
        // A trasa 100 kroków da (10000 - 1000) = 9000
        // Krótsza trasa ma WIĘKSZY fitness.
        fitness += 10000f;
        fitness -= (path.Count * 10f); // Silna presja na krótki czas
    }
    else
    {
        // Jeśli nie dotarł, fitness zależy TYLKO od bliskości.
        // Używamy funkcji wykładniczej lub silnie liniowej, by promować bycie blisko.
        // Max nagroda za bycie tuż obok to np. 1000 (dużo mniej niż za sukces).
        float progress = 1f - (dist / startDist);
        fitness += progress * 1000f;
    }

    // --- PRIORYTET 2: KARY ZA STYL (Fine-tuning) ---
    // Te kary powinny być małe, żeby nie zagłuszyć głównego celu (dystansu)
    foreach (var n in path)
    {
        // Zamiast sprawdzać Walkable (bo Apply tego pilnuje), 
        // karzemy za akcje, które nie przynoszą efektu (czekanie)
        
        if (n.action == RobotAction.Wait)
        {
            fitness -= 2f; // Mała kara (robot ma prawo czekać, jeśli musi, ale nie bez powodu)
        }
        else if (n.action == RobotAction.TurnLeft || n.action == RobotAction.TurnRight)
        {
            fitness -= 1f; // Preferujemy proste linie, ale skręty są tanie
        }
        
        // Opcjonalnie: Kara za brak postępu
        // Jeśli robot uderzył w ścianę (pozycja się nie zmieniła mimo akcji Forward),
        // warto to wykryć porównując z poprzednim węzłem, ale to wymaga pętli for(int i=1...)
    }

    // Wynik nie może być ujemny (dla bezpieczeństwa selekcji ruletkowej)
    return Mathf.Max(0.1f, fitness);
}
    (List<RobotAction>, List<Node>, float) GA2_TournamentSelection((List<RobotAction>, List<Node>, float)[] pop)
    {
        var best = pop[UnityEngine.Random.Range(0, pop.Length)];

        for (int i = 1; i < GA2tournamentSize; i++)
        {
            var contender = pop[UnityEngine.Random.Range(0, pop.Length)];
            if (contender.Item3 > best.Item3) // Porównujemy Fitness (Item3)
            {
                best = contender;
            }
        }
        return best;
    }
    // Selekcja Rangowa (wymaga posortowanej populacji!)
    (List<RobotAction>, List<Node>, float) GA2_RankSelection((List<RobotAction>, List<Node>, float)[] sortedPop)
    {
        int N = sortedPop.Length;

        // Suma ciągu arytmetycznego: N + (N-1) + ... + 1
        // Wzór: S = N * (N + 1) / 2
        // Przykład: dla N=100, suma wag wynosi 5050.
        int sumOfWeights = (N * (N + 1)) / 2;

        // Losujemy punkt na kole ruletki (od 0 do sumy wag)
        int randomPoint = UnityEngine.Random.Range(0, sumOfWeights);

        // Szukamy właściciela tego punktu
        for (int i = 0; i < N; i++)
        {
            // Waga osobnika zależy od jego pozycji w rankingu.
            // Ponieważ lista jest posortowana malejąco (0 = najlepszy):
            // Osobnik 0 ma wagę N (np. 100)
            // Osobnik 1 ma wagę N-1 (np. 99)
            // ...
            // Osobnik ostatni ma wagę 1
            int weight = N - i;

            randomPoint -= weight;

            // Jeśli zeszliśmy poniżej zera, to znaczy, że wylosowaliśmy tego osobnika
            if (randomPoint < 0)
            {
                return sortedPop[i];
            }
        }

        // Zabezpieczenie (teoretycznie nieosiągalne), zwracamy najlepszego
        return sortedPop[0];
    }
    void GA2_Mutate(List<RobotAction> actions)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            if (UnityEngine.Random.value < GA2mutationRate)
            {
                // Pobieramy losową akcję z Enuma
                var values = System.Enum.GetValues(typeof(RobotAction));
                actions[i] = (RobotAction)values.GetValue(UnityEngine.Random.Range(0, values.Length));
            }
        }
    }
    List<RobotAction> GA2_Crossover(List<RobotAction> parentA, List<RobotAction> parentB)
    {
        List<RobotAction> child = new List<RobotAction>();

        // Zabezpieczenie przed pustymi listami
        int countA = parentA.Count;
        int countB = parentB.Count;
        if (countA == 0) return new List<RobotAction>(parentB);
        if (countB == 0) return new List<RobotAction>(parentA);

        // Punkt cięcia losujemy na podstawie krótszego rodzica (bezpieczniej)
        int minLen = Mathf.Min(countA, countB);
        int splitPoint = UnityEngine.Random.Range(1, minLen);

        // Część 1 od Rodzica A
        for (int i = 0; i < splitPoint; i++)
        {
            child.Add(parentA[i]);
        }
        // Część 2 od Rodzica B
        for (int i = splitPoint; i < countB; i++)
        {
            child.Add(parentB[i]);
        }

        // Opcjonalnie: przytnij do maxSteps
        if (child.Count > GA2maxSteps)
            child = child.GetRange(0, GA2maxSteps);

        return child;
    }
    // Zamienia listę akcji na ścieżkę i oblicza jej fitness
    (List<RobotAction>, List<Node>, float) GA2_SimulatePath(List<RobotAction> actions, Tile start, Tile goal, Heading startHead, int startStep)
    {
        List<Node> path = new List<Node>();
        Node cur = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        path.Add(cur);

        // Symulujemy ruchy
        // Ważne: Jeśli lista akcji jest dłuższa niż maxSteps, ucinamy ją
        // Jeśli dotrzemy do celu, przerywamy pętlę
        int limit = Mathf.Min(actions.Count, GA2maxSteps);

        for (int i = 0; i < limit; i++)
        {
            RobotAction action = actions[i];

            // Sprawdzamy czy ruch jest możliwy
            (Node next, bool allowed) = Apply(cur, action);

            if (allowed)
            {
                cur = next;
                path.Add(cur);
            }
            // Else: Robot stoi w miejscu, ale akcja "zużyta" (można tu dodać karę)

            // Czy cel osiągnięty?
            if (cur.x == goal.x && cur.y == goal.y)
            {
                // Opcjonalnie: Ucinamy resztę akcji, bo już dotarliśmy
                // actions = actions.GetRange(0, i + 1); 
                break;
            }
        }

        float fitness = GA2_Fitness(path, goal, start);
        return (actions, path, fitness);
    }
    // To zastępuje Twoją obecną funkcję GA2_SetStartPath (lub możesz użyć jej jako alternatywy)
    (List<RobotAction>, List<Node>, float) GA2_SetStartPath_Camel(Tile start, Tile goal, Heading startHead, int startStep)
    {
        List<Node> path = new List<Node>();
        List<RobotAction> actionsList = new List<RobotAction>();

        Node cur = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
        path.Add(cur); // Dodajemy stan początkowy

        // Lista dostępnych akcji do iteracji
        RobotAction[] possibleActions = { RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait };
        int maxSteps = GA2maxSteps;
        int stepsTaken = 0;

        while (!(cur.x == goal.x && cur.y == goal.y) && stepsTaken < maxSteps)
        {
            // 1. Obliczanie wag dla każdej akcji
            float[] weights = new float[possibleActions.Length];
            float sumWeights = 0f;

            // Tablica pomocnicza przechowująca wynik Apply dla każdej akcji, żeby nie liczyć dwa razy
            Node[] nextNodes = new Node[possibleActions.Length];
            bool[] isAllowed = new bool[possibleActions.Length];

            for (int i = 0; i < possibleActions.Length; i++)
            {
                RobotAction action = possibleActions[i];
                (Node nxt, bool ok) = Apply(cur, action);

                nextNodes[i] = nxt;
                isAllowed[i] = ok;

                if (!ok)
                {
                    weights[i] = 0f;
                    continue;
                }

                // --- HEURYSTYKA CAMELA ---
                float w = 0f;

                // Bazowa ocena ruchu (czy przybliża do celu + czy patrzy na cel)
                float h = GA2Camel_HeuristicDesirability(cur, nxt, (goal.x, goal.y));
                w = h;

                // --- LOOK AHEAD (Logika z Twojego kodu) ---
                // Jeśli skręcamy, sprawdzamy czy po skręcie jest sens iść do przodu
                if (action == RobotAction.TurnLeft || action == RobotAction.TurnRight)
                {
                    // Symulujemy krok do przodu z nowej pozycji
                    (Node nxtfwd, bool okf) = Apply(nxt, RobotAction.Forward);

                    if (okf)
                    {
                        // Jeśli po skręcie można iść, dodajemy ocenę tego przyszłego pola
                        float h2 = GA2Camel_HeuristicDesirability(nxt, nxtfwd, (goal.x, goal.y));

                        // Mieszamy ocenę skrętu z oceną przyszłego kroku
                        w += h2;
                        w *= 0.5f; // Lekkie tłumienie, żeby nie faworyzować skrętów nadmiernie
                    }
                    else
                    {
                        // Jeśli skręcamy w ścianę -> drastycznie zmniejszamy wagę
                        w *= 0.1f;
                    }
                }
                else if (action == RobotAction.Wait)
                {
                    // Czekanie jest ostatecznością
                    w = 0.1f;
                }

                // Zabezpieczenie: minimalna waga, by algorytm nie utknął, jeśli wszystkie ruchy są złe
                // (pozwala na losowe błądzenie w sytuacjach krytycznych)
                if (w < 0.1f) w = 0.1f;

                weights[i] = w;
                sumWeights += w;
            }

            // Jeśli nie ma żadnych ruchów (sumWeights bliskie 0), przerywamy
            if (sumWeights <= 0.001f) break;

            // 2. Wybór metodą ruletki (Roulette Wheel Selection)
            float r = UnityEngine.Random.value * sumWeights;
            float acc = 0f;
            int chosenIndex = 0; // Domyślnie Forward (index 0)

            for (int i = 0; i < possibleActions.Length; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    chosenIndex = i;
                    break;
                }
            }

            // 3. Aplikowanie wyboru
            if (isAllowed[chosenIndex])
            {
                cur = nextNodes[chosenIndex];
                path.Add(cur);
                actionsList.Add(possibleActions[chosenIndex]);
                stepsTaken++;
            }
            else
            {
                // Teoretycznie niemożliwe dzięki if(sumWeights), ale dla bezpieczeństwa:
                break;
            }
        }

        // 4. Obliczamy Fitness dla GA (używając Twojej ulepszonej funkcji)
        float fitness = GA2_Fitness(path, goal, start);

        return (actionsList, path, fitness);
    }

    // Pomocnicza funkcja heurystyki (lekko oczyszczona z Twojego kodu)
    float GA2Camel_HeuristicDesirability(Node c, Node n, (int gx, int gy) goal)
    {
        float curManhattan = Mathf.Abs(c.x - goal.gx) + Mathf.Abs(c.y - goal.gy);
        float nexManhattan = Mathf.Abs(n.x - goal.gx) + Mathf.Abs(n.y - goal.gy);

        // Premia za kierunek (Facing)
        float facing = 1f;
        int dx = goal.gx - n.x;
        int dy = goal.gy - n.y;

        if ((n.head == Heading.East && dx > 0) ||
            (n.head == Heading.West && dx < 0) ||
            (n.head == Heading.North && dy > 0) ||
            (n.head == Heading.South && dy < 0))
        {
            facing = 5.0f; // Zwiększyłem bonus za patrzenie w dobrą stronę
        }

        // Jeśli dystans maleje, wynik jest dodatni. Jeśli rośnie - ujemny (clamped to 0).
        // Mnożnik 10.0f nadaje wagę odległości.
        float distDiff = (curManhattan - nexManhattan) * 10f;

        // Wynik nie może być ujemny dla ruletki
        float result = distDiff + facing;
        return Mathf.Max(0.1f, result);
    }
}