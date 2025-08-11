using UnityEngine;
using UnityEngine.InputSystem; // NOWY INPUT

public class FreeFlyCamera : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float boostMultiplier = 2f;
    public float lookSensitivity = 0.15f; // stopnie na piksel (zmniejsz/wiêksz wg uznania)

    float yaw, pitch;
    bool mouseCaptured = true;

    void Start()
    {
        var e = transform.eulerAngles;
        yaw = e.y; pitch = e.x;
        SetCursor(true);
    }

    void OnDisable() => SetCursor(false);

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        // Toggle „chwytania” myszy
        if (kb.escapeKey.wasPressedThisFrame) SetCursor(!mouseCaptured);

        // Mysz: obrót kamery
        if (mouseCaptured)
        {
            Vector2 md = mouse.delta.ReadValue();   // piksele od ostatniej klatki
            yaw += md.x * lookSensitivity;
            pitch -= md.y * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        // Klawiatura: ruch
        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? boostMultiplier : 1f);
        Vector3 move = Vector3.zero;

        if (kb.wKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed) move -= transform.forward;
        if (kb.aKey.isPressed) move -= transform.right;
        if (kb.dKey.isPressed) move += transform.right;
        if (kb.spaceKey.isPressed) move += transform.up;                              // w górê
        if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) move -= transform.up; // w dó³

        if (move.sqrMagnitude > 1f) move.Normalize();
        transform.position += move * speed * Time.deltaTime;
    }

    void SetCursor(bool capture)
    {
        mouseCaptured = capture;
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }
}
