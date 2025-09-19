using UnityEngine;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    public Camera freeCamera;
    public Camera topCamera;
    public bool startWithFree = true;

    [Header("Keybinds")]
    public Key cameraToggleKey = Key.E;
    public Key cursorToggleKey = Key.Tab;

    FreeCamera freeCtrl;
    bool cursorEnabled = false;
    public void Init(GameManager gm)
    {
        // Na razie puste, ale zostawiamy dla spójnoœci
    }

    void Awake()
    {
        if (!freeCamera) freeCamera = Camera.main;
        freeCtrl = freeCamera ? freeCamera.GetComponent<FreeCamera>() : null;

        SetActive(startWithFree ? freeCamera : topCamera);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Prze³¹czanie kamer
        if (kb[cameraToggleKey].wasPressedThisFrame)
            SetActive(topCamera.enabled ? freeCamera : topCamera);

        // Toggle kursora
        if (kb[cursorToggleKey].wasPressedThisFrame)
            ToggleCursor();
    }

    void SetActive(Camera active)
    {
        bool useFree = (active == freeCamera);

        // Kamery
        freeCamera.enabled = useFree;
        topCamera.enabled = !useFree;

        // Sterownik free-cam tylko gdy free
        if (freeCtrl) freeCtrl.enabled = useFree;

        // AudioListener
        var alFree = freeCamera.GetComponent<AudioListener>();
        var alOver = topCamera.GetComponent<AudioListener>();
        if (alFree) alFree.enabled = useFree;
        if (alOver) alOver.enabled = !useFree;

        // Reset kursora przy zmianie kamery
        if (useFree && !cursorEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
    public Camera GetActiveCamera()
    {
        return freeCamera.enabled ? freeCamera : topCamera;
    }



    void ToggleCursor()
    {
        cursorEnabled = !cursorEnabled;

        if (cursorEnabled)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (freeCtrl) freeCtrl.SetFrozen(true);
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (freeCtrl) freeCtrl.SetFrozen(false);
        }
    }

}
