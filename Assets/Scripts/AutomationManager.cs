using System.Collections; // Potrzebne do Coroutines
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
    public Key standardCyclePath = Key.J;
    public Key nextStep = Key.RightArrow;
    public Key toggleAutoKey = Key.H; // Nowy klawisz do w³¹czania automatu

    public int currentStep = 0;
    private List<Tile[,]> RTgrid; // Real time grid

    [Header("Ruch robota")]
    [SerializeField] private float moveTime = 0.15f;

    [Header("Auto-repeat nextStep (Input manualny)")]
    [SerializeField] private float initialDelay = 0.35f;
    [SerializeField] private float repeatRate = 0.07f;

    [Header("Pe³na Automatyzacja")]
    [SerializeField] private float autoStepInterval = 1.0f; // Co ile sekund krok (1s)
    [SerializeField] private int cycleTriggerInterval = 4;  // Co ile kroków nowa œcie¿ka (4)
    private bool isAutoRunning = false;

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

        // --- H: W³¹cz/Wy³¹cz pe³n¹ automatyzacjê ---
        if (kb[toggleAutoKey].wasPressedThisFrame)
        {
            isAutoRunning = !isAutoRunning;
            if (isAutoRunning)
            {
                Debug.Log("[AutomationManager] Start automatyzacji.");
                StartCoroutine(AutoLoop());
            }
            else
            {
                Debug.Log("[AutomationManager] Stop automatyzacji.");
                StopAllCoroutines(); // Zatrzymuje pêtlê
            }
        }

        // --- G: œcie¿ka standardowa (jeden strza³) ---
        if (kb[standardPath].wasPressedThisFrame)
        {
            gm.robotManager.AssignShelfPath(currentStep);
            gm.robotManager.MoveAllRobots(currentStep, moveTime);
        }

        // --- J: jedna marszruta losowa  ---
        if (kb[standardCyclePath].wasPressedThisFrame)
        {
            gm.robotManager.AssignStandardCyclePath(currentStep);
            gm.robotManager.MoveAllRobots(currentStep, moveTime);
        }

        // Jeœli automat dzia³a, blokujemy manualne sterowanie strza³k¹ (opcjonalnie)
        if (isAutoRunning) return;

        /// --- Auto-repeat dla nextStep (RightArrow) - Manualne ---
        var keyNext = kb[nextStep];

        bool justPressed = keyNext.wasPressedThisFrame;
        bool pressed = keyNext.isPressed;
        bool justReleased = keyNext.wasReleasedThisFrame;

        if (justPressed)
        {
            ExecuteStepOnce();
            isHoldingNext = true;
            holdTimerNext = 0f;
        }
        else if (pressed && isHoldingNext)
        {
            holdTimerNext += Time.deltaTime;
            if (holdTimerNext >= initialDelay)
            {
                holdTimerNext -= repeatRate;
                ExecuteStepOnce();
            }
        }
        else if (justReleased || !pressed)
        {
            isHoldingNext = false;
            holdTimerNext = 0f;
        }
        ///
    }

    // --- Pêtla automatyzacji ---
    IEnumerator AutoLoop()
    {
        while (isAutoRunning)
        {
            // 1. Czekaj 1 sekundê
            yield return new WaitForSeconds(autoStepInterval);

            // Zabezpieczenie, gdyby wy³¹czono w trakcie czekania
            if (!isAutoRunning) break;

            // 2. Co 4 kroki wywo³aj Set Standard Cycle
            // U¿ywamy modulo (reszta z dzielenia)
            if (currentStep % cycleTriggerInterval == 0)
            {
                //Debug.Log($"[AutoMan] Cykl {cycleTriggerInterval} kroków - przydzielanie œcie¿ek.");
                gm.robotManager.AssignStandardCyclePath(currentStep);
            }

            // 3. Wykonaj krok (to równie¿ ruszy robotami po nowej œcie¿ce, jeœli zosta³a przydzielona)
            ExecuteStepOnce();
        }
    }

    private void ExecuteStepOnce()
    {
        currentStep++;
        Debug.Log("[AutomationManager] Aktualny step: " + currentStep);

        // --- UpdateRTgrid ---
        var t0 = Time.realtimeSinceStartup;
        gm.gridManager.UpdateRTgrid(currentStep);

        // --- MoveAllRobots ---
        // To wykona ruch bazuj¹c na œcie¿ce przydzielonej chwilê wczeœniej w AutoLoop (jeœli wypad³ 4 krok)
        gm.robotManager.MoveAllRobots(currentStep, moveTime);

        // --- RefreshAll ---
        gm.gridManager.RefreshAll(currentStep);

        Debug.Log($"[AutoMan] Total step time: {(Time.realtimeSinceStartup - t0) * 1000f} ms");
    }
}