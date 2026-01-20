using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;

public static class ConvergenceLogger
{
    public static void Log(
        string algorithm,
        int iteration,
        float timeMs,
        int manhattan,
        float fitness,
        int bestPathLength)
    {
        string fileName = algorithm + "ConvergenceLog.csv"; // np. "FAConvergenceLog.csv"
        string filePath = Path.Combine(Application.dataPath, fileName);

        bool exists = File.Exists(filePath);

        using (var sw = new StreamWriter(filePath, true, Encoding.UTF8))
        {
            if (!exists)
            {
                sw.WriteLine("Algorithm;Iteration;TimeMs;Manhattan;Fitness;BestPathLength");
            }

            string fitnessStr = fitness.ToString("F4", CultureInfo.InvariantCulture);

            sw.WriteLine(
                $"{algorithm};" +
                $"{iteration};" +
                $"{timeMs.ToString("F2", CultureInfo.InvariantCulture)};" +
                $"{manhattan};" +
                $"{fitnessStr};" +
                $"{bestPathLength}");
        }
    }
}
