using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    // --- Parametry Algorytmu Genetycznego (GA) ---
    [Header("Genetic Algorithm")]
    public int populationSize = 100;
    public int gaIterations = 100;
    [Range(0.01f, 0.9f)] public float crossoverRate = 0.8f; // Prawdopodobieństwo krzyżowania
    [Range(0.001f, 0.5f)] public float mutationRate = 0.05f; // Prawdopodobieństwo mutacji
    public int tournamentSize = 5; // Parametr dla selekcji turniejowej
    public int maxSteps = 100; // Maksymalna długość ścieżki/chromosomu

    // --- Struktura Chromosomu (Ścieżki) ---
    public class PathChromosome
    {
        public List<RobotAction> actions;
        public float fitness;
        public List<Node> finalNodes;

        public PathChromosome()
        {
            actions = new List<RobotAction>();
            finalNodes = new List<Node>();
            fitness = 0f;
        }

        public PathChromosome Clone()
        {
            return new PathChromosome
            {
                actions = new List<RobotAction>(actions),
                fitness = fitness,
                // finalNodes nie musi być klonowane, bo jest generowane przy ocenie
            };
        }
    }

    // ====================================================================
    // 1. GŁÓWNA METODA STARTUJĄCA (Wywoływana z PathManager.cs)
    // ====================================================================

    void GA_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    {
        StartCoroutine(GA_Coroutine(start, goal, startHead, startStep, robot, path =>
        {
            if (path == null || path.Count == 0)
            {
                Debug.LogError($"No path found for robot {robot.Id} to point.");
                return;
            }

            // Wizualizacja najlepszej ścieżki
            // Zaznaczanie komórek w RTgrid jako BestAlgPath
            //for (int i = 0; i < path.Count; i++)
            //{
            //    if (path[i].step < 0 || path[i].step >= RTgrid.Count) continue;
            //    // Zakładamy, że RTgrid[step] nie jest null
            //    if (RTgrid[path[i].step][path[i].x, path[i].y] != null)
            //    {
            //        RTgrid[path[i].step][path[i].x, path[i].y].flags |= TileFlags.BestAlgPath;
            //    }
            //}
            //gm.gridManager.RefreshAll(startStep);

            Debug.LogWarning($"Assigning point path to robot {robot.Id}, path size: {path.Count}");
            gm.robotManager.AssignPlanToRobot(robot, path);
            robot.destinations.Dequeue();
        }));
    }

    // ====================================================================
    // 2. GŁÓWNY KORUTYNA ALGORYTMU GENETYCZNEGO
    // ====================================================================

    IEnumerator GA_Coroutine(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot, System.Action<List<Node>> onDone)
    {
        float t0 = Time.realtimeSinceStartup;

        // 1. Inicjalizacja
        // UWAGA: Użycie startHead.
        List<PathChromosome> population = InitializePopulation(start, startHead, startStep);
        PathChromosome bestChromosome = new PathChromosome();

        // Zapewnienie minimalnej wielkości siatki RTgrid dla symulacji
        //gm.gridManager.UpdateRTgrid(startStep + maxSteps);

        for (int it = 0; it < gaIterations; it++)
        {
            float tIter = Time.realtimeSinceStartup;

            // 2. Ocena (Fitness)
            // UWAGA: Użycie startHead.
            EvaluatePopulation(population, start, startHead, (goal.x, goal.y), startStep);

            // Znajdź najlepsze rozwiązanie w obecnej populacji
            var currentBest = GetBestChromosome(population);
            if (currentBest.fitness > bestChromosome.fitness)
            {
                // Przechowujemy klon, aby zapobiec modyfikacjom
                bestChromosome = currentBest.Clone();
            }

            // Opcjonalne: Sprawdź wczesne zakończenie
            if (bestChromosome.finalNodes != null && bestChromosome.finalNodes.Count > 0)
            {
                // Musimy upewnić się, że ostatni węzeł faktycznie osiągnął cel (lub jest blisko)
                var lastNode = bestChromosome.finalNodes[^1];
                if (lastNode.x == goal.x && lastNode.y == goal.y)
                {
                    Debug.Log($"[GA] Early exit in iteration {it}. Goal reached.");
                    break;
                }
            }

            // 3. Selekcja i Tworzenie Nowej Generacji
            List<PathChromosome> newPopulation = new List<PathChromosome>(populationSize);

            // Elitaryzm: Utrzymanie najlepszego rozwiązania
            newPopulation.Add(bestChromosome.Clone());

            while (newPopulation.Count < populationSize)
            {
                // Selekcja Turniejowa (Wybierz 2 rodziców)
                PathChromosome parent1 = TournamentSelection(population).Clone();
                PathChromosome parent2 = TournamentSelection(population).Clone();

                // Krzyżowanie (Crossover)
                (PathChromosome child1, PathChromosome child2) = Crossover(parent1, parent2);

                // Mutacja
                Mutate(child1);
                Mutate(child2);

                newPopulation.Add(child1);
                if (newPopulation.Count < populationSize) newPopulation.Add(child2);
            }

            population = newPopulation;
            Debug.Log($"[GA] Iter {it} took {(Time.realtimeSinceStartup - tIter) * 1000f} ms. Best Fitness: {bestChromosome.fitness:F2}");
            Debug.Log($"[GA] Best path length: {bestChromosome.actions.Count}");
            for (int a = 0; a < bestChromosome.actions.Count; a+=5)
            {
                Debug.Log($"    Step {a}: {bestChromosome.actions[a]}");
            }

            foreach (var n in bestChromosome.finalNodes)
                RTgrid[startStep][n.x, n.y].flags |= TileFlags.BestAlgPath;
            gm.gridManager.RefreshAll(startStep);

            while (Keyboard.current[nextIterationKey].isPressed)
                yield return null;

            // 2️⃣ Teraz czekamy na naciśnięcie (prawidłowe przejście do następnej iteracji)
            while (!Keyboard.current[nextIterationKey].wasPressedThisFrame)
                yield return null;

            yield return null; // Odczekaj jedną klatkę
        }
        

        // Zakończenie
        float elapsed = (Time.realtimeSinceStartup - t0) * 1000f;
        Debug.LogWarning($"[GA Timer] GA for robot {robot.Id} took {elapsed:F2} ms");

        onDone?.Invoke(bestChromosome.finalNodes);
    }

    // ====================================================================
    // 3. OPERATORY GENETYCZNE
    // ====================================================================

    // --- Inicjalizacja ---
    List<PathChromosome> InitializePopulation(Tile start, Heading startHead, int startStep) // Dodano startHead
    {
        var population = new List<PathChromosome>(populationSize);
        // Akcje: Forward, TurnLeft, TurnRight, Wait
        var possibleActions = new RobotAction[] { RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait };

        for (int i = 0; i < populationSize; i++)
        {
            var chromosome = new PathChromosome();
            for (int j = 0; j < maxSteps; j++)
            {
                chromosome.actions.Add(possibleActions[UnityEngine.Random.Range(0, possibleActions.Length)]);
            }
            population.Add(chromosome);
        }
        return population;
    }

    // --- Ocena Fitness ---
    void EvaluatePopulation(List<PathChromosome> population, Tile start, Heading startHead, (int gx, int gy) goal, int startStep) // Dodano startHead
    {
        foreach (var chromosome in population)
        {
            // POPRAWKA BŁĘDU CS1061: Używamy startHead
            Node current = new Node(start.x, start.y, startHead, RobotAction.Wait, startStep);
            var nodes = new List<Node> { current };
            int pathLength = 0;
            bool reachedGoal = false;
            float collisionPenalty = 0f;

            // Symulacja ścieżki
            for (int i = 0; i < maxSteps; i++)
            {
                RobotAction action = i < chromosome.actions.Count ? chromosome.actions[i] : RobotAction.Wait; //???

                var (next, allowed) = Apply(current, action);

                // Sprawdzenie kolizji ze ścianami/granicami
                if (!allowed)
                {
                    collisionPenalty += 500f;
                    pathLength = i;
                    break;
                }

                // Sprawdzenie kolizji w czasie rzeczywistym (z innymi robotami)
                int nextTimeStep = i + startStep + 1;

                if (nextTimeStep < RTgrid.Count)
                {
                    // Sprawdzenie czy komórka jest w granicach siatki RTgrid[nextTimeStep]
                    int gridWidth = RTgrid[nextTimeStep].GetLength(0);
                    int gridHeight = RTgrid[nextTimeStep].GetLength(1);

                    if (next.x >= 0 && next.x < gridWidth && next.y >= 0 && next.y < gridHeight)
                    {
                        if (RTgrid[nextTimeStep][next.x, next.y].flags.HasFlag(TileFlags.Blocked))
                        {
                            collisionPenalty += 1000f;
                            // Kontynuujemy, ale z karą
                        }
                    }
                }

                current = next;
                nodes.Add(current);
                pathLength++;

                if (current.x == goal.gx && current.y == goal.gy)
                {
                    reachedGoal = true;
                    break;
                }
            }

            // Obliczanie Fitness
            float distToGoal = Vector2.Distance(new Vector2(current.x, current.y), new Vector2(goal.gx, goal.gy));
            float fitness = 0f;

            if (reachedGoal)
            {
                // Nagroda za dotarcie + nagroda za krótkość
                fitness = 2000f + (maxSteps - pathLength);
            }
            else
            {
                // Kara za odległość od celu
                fitness = 1000f - distToGoal * 10f;
            }

            // Odejmij kary
            chromosome.fitness = fitness - collisionPenalty;
            chromosome.finalNodes = nodes;
        }
    }

    // --- Znajdź Najlepszy Chromosom ---
    PathChromosome GetBestChromosome(List<PathChromosome> population)
    {
        PathChromosome best = population[0];
        foreach (var c in population)
        {
            if (c.fitness > best.fitness)
            {
                best = c;
            }
        }
        return best;
    }

    // --- Selekcja Turniejowa ---
    PathChromosome TournamentSelection(List<PathChromosome> population)
    {
        PathChromosome best = population[0];

        for (int i = 0; i < tournamentSize; i++)
        {
            int r = UnityEngine.Random.Range(0, population.Count);
            PathChromosome candidate = population[r];

            if (candidate.fitness > best.fitness)
            {
                best = candidate;
            }
        }
        return best;
    }

    // --- Krzyżowanie (Single-Point Crossover) ---
    (PathChromosome, PathChromosome) Crossover(PathChromosome p1, PathChromosome p2)
    {
        if (UnityEngine.Random.value > crossoverRate)
        {
            return (p1.Clone(), p2.Clone());
        }

        int crossPoint = UnityEngine.Random.Range(1, maxSteps);
        PathChromosome child1 = new PathChromosome();
        PathChromosome child2 = new PathChromosome();

        for (int i = 0; i < maxSteps; i++)
        {
            if (i < crossPoint)
            {
                child1.actions.Add(p1.actions[i]);
                child2.actions.Add(p2.actions[i]);
            }
            else
            {
                child1.actions.Add(p2.actions[i]);
                child2.actions.Add(p1.actions[i]);
            }
        }

        return (child1, child2);
    }

    // --- Mutacja ---
    void Mutate(PathChromosome chromosome)
    {
        var possibleActions = new RobotAction[] { RobotAction.Forward, RobotAction.TurnLeft, RobotAction.TurnRight, RobotAction.Wait };

        for (int i = 0; i < chromosome.actions.Count; i++)
        {
            if (UnityEngine.Random.value < mutationRate)
            {
                chromosome.actions[i] = possibleActions[UnityEngine.Random.Range(0, possibleActions.Length)];
            }
        }
    }
}