// Assets/Scripts/Cameras/CameraSwitcher.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraManager : MonoBehaviour
{
    public Camera freeCamera;
    public Camera topCamera;
    public bool startWithFree = true;
    public Key toggleKey = Key.E;


    FreeCamera freeCtrl;

    void Awake()
    {
        if (!freeCamera) freeCamera = Camera.main;
        freeCtrl = freeCamera ? freeCamera.GetComponent<FreeCamera>() : null;

        SetActive(startWithFree ? freeCamera : topCamera);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame)
            SetActive(topCamera.enabled ? freeCamera : topCamera);
    }

    void SetActive(Camera active)
    {
        bool useFree = (active == freeCamera);

        // Kamery
        freeCamera.enabled = useFree;
        topCamera.enabled = !useFree;

        // Sterownik free-cam tylko gdy free
        if (freeCtrl) freeCtrl.enabled = useFree;

        // Jeden AudioListener
        var alFree = freeCamera.GetComponent<AudioListener>();
        var alOver = topCamera.GetComponent<AudioListener>();
        if (alFree) alFree.enabled = useFree;
        if (alOver) alOver.enabled = !useFree;

        // Cursor modes
        if (useFree)
        {
            Cursor.lockState = CursorLockMode.Locked; // kursor ukryty i zablokowany
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;   // kursor widoczny i odblokowany
            Cursor.visible = true;
        }
    }
}
