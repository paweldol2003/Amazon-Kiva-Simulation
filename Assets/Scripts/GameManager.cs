using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Modules")]
    public CameraManager cameraManager;
    public SelectionManager selectionManager;
    public RobotManager robotManager;
    public GridManager gridManager;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Inicjalizacja modu³ów
        if (cameraManager != null) cameraManager.Init(this);
        if (selectionManager != null) selectionManager.Init(this);
        if (robotManager != null) robotManager.Init(this);
        if (gridManager != null) gridManager.Init(this);

        Debug.Log("[GameManager] Initialized");
    }
}
