using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Modules")]
    public CameraManager cameraManager;
    public SelectionManager selectionManager;
    public RobotManager robotManager;
    public GridManager gridManager;
    public PathManager pathManager;
    public AutomationManager automationManager;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Inicjalizacja modu³ów
        if (gridManager != null) gridManager.Init(this);
        if (cameraManager != null) cameraManager.Init(this);
        if (selectionManager != null) selectionManager.Init(this);
        if (robotManager != null) robotManager.Init(this);
        if (pathManager != null) pathManager.Init(this);
        if (automationManager != null) automationManager.Init(this);



        Debug.Log("[GameManager] Initialized");
    }
}
