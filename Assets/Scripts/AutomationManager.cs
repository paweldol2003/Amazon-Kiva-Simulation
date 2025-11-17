using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class AutomationManager : MonoBehaviour
{
    private GameManager gm;
    public void Init(GameManager gm) => this.gm = gm;

    [Header("Keybinds")]
    public Key modeSwitch = Key.M;
    public Key randomPath = Key.F;
    public Key standardPath = Key.G;
    public Key nextStep = Key.RightArrow;

    public int currentStep = 0;
    private List<Tile[,]> RTgrid; // Real time grid

    [Header("Ruch robota")]
    [SerializeField] private float moveTime = 0.15f;

    [Header("Auto-repeat nextStep (jak w Windowsie)")]
    [SerializeField] private float initialDelay = 0.35f; // pierwszy repeat po 350 ms
    [SerializeField] private float repeatRate = 0.07f; // kolejne powtórzenia co 70 ms

    private float holdTimerNext = 0f;
    private bool isHoldingNext = false;

    void Start()
    {
        RTgrid = gm.gridManager.RTgrid;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // --- G: œcie¿ka standardowa (jeden strza³) ---
        if (kb[standardPath].wasPressedThisFrame)
        {
            gm.pathManager.SetStandardPath(currentStep);
            gm.robotManager.MoveAllRobots(currentStep, moveTime);
        }

        /// --- Auto-repeat dla nextStep (RightArrow) ---
        var keyNext = kb[nextStep];

        bool justPressed = keyNext.wasPressedThisFrame;
        bool pressed = keyNext.isPressed;
        bool justReleased = keyNext.wasReleasedThisFrame;

        // 1) Jednorazowe naciœniêcie – od razu jeden krok
        if (justPressed)
        {
            ExecuteStepOnce();

            isHoldingNext = true;
            holdTimerNext = 0f; // start liczenia do pierwszego repeatu
        }
        // 2) Klawisz trzymany – auto-repeat po initialDelay, potem co repeatRate
        else if (pressed && isHoldingNext)
        {
            holdTimerNext += Time.deltaTime;

            if (holdTimerNext >= initialDelay)
            {
                // po pierwszym opóŸnieniu powtarzamy co repeatRate
                holdTimerNext -= repeatRate; // zostaw nadmiar, ¿eby tempo by³o równe
                ExecuteStepOnce();
            }
        }
        // 3) Puszczenie klawisza – reset
        else if (justReleased || !pressed)
        {
            isHoldingNext = false;
            holdTimerNext = 0f;
        }
        ///
    }

    private void ExecuteStepOnce()
    {
        currentStep++;
        Debug.Log("[AutomationManager] Aktualny step: " + currentStep);

        gm.gridManager.UpdateRTgrid(currentStep);
        gm.robotManager.MoveAllRobots(currentStep, moveTime);
        gm.gridManager.CheckSpawnpointsOccupation(currentStep);
        gm.gridManager.RefreshAll(currentStep);

        Debug.Log($"Iloœæ gridów: {RTgrid.Count}");
    }
}
