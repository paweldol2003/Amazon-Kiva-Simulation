using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class PathManager : MonoBehaviour
{
    [Header("Firefly")]
    public int fireflies = 40;
    public int fireiterations = 60;
    [Range(0.1f, 5f)] public float firealpha = 1.0f;     // waga feromonów


    void Firefly_Start(Tile start, Tile goal, Heading startHead, int startStep, RobotController robot)
    { }
}