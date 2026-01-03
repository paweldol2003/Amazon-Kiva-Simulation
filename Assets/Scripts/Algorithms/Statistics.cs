using System.IO;
using System.Text;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public static class AlgorithmLogger
{
    // Œcie¿ka: folder Twojego projektu / AlgorithmResults.csv
    private static string filePath = Application.dataPath + "/AlgorithmResults.csv";

    public static void LogToCSV(string algoName, float timeMs, int pathLength, int rotations, bool success, int step, int manhattan)
    {
        bool fileExists = File.Exists(filePath);
        using (StreamWriter sw = new StreamWriter(filePath, true, Encoding.UTF8))
        {
            if (!fileExists)
            {
                // Dodajemy Manhattan do nag³ówka
                sw.WriteLine("Algorithm;TimeMs;PathLength;Rotations;Success;Step;Manhattan");
            }
            // Zapisujemy wartoœæ
            sw.WriteLine($"{algoName};{timeMs:F2};{pathLength};{rotations};{success};{step};{manhattan}");
        }
    }
}