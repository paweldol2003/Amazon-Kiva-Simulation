// Assets/Scripts/Cameras/FreeFlyCamera.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCamera : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float boostMultiplier = 2f;
    public float lookSensitivity = 0.15f; // stopnie na piksel

    float yaw, pitch;

    void Start()
    {
        var e = transform.eulerAngles;
        yaw = e.y; pitch = e.x;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        // Mysz: zawsze rozglądanie (cursor lock zrobi CameraSwitcher)
        Vector2 md = mouse.delta.ReadValue();
        yaw += md.x * lookSensitivity;
        pitch -= md.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Ruch: WASD + Space (↑) + Ctrl (↓) + Shift (boost)
        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? boostMultiplier : 1f);
        Vector3 move = Vector3.zero;

        if (kb.wKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed) move -= transform.forward;
        if (kb.aKey.isPressed) move -= transform.right;
        if (kb.dKey.isPressed) move += transform.right;
        if (kb.spaceKey.isPressed) move += transform.up;
        if (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed) move -= transform.up;

        if (move.sqrMagnitude > 1f) move.Normalize();
        transform.position += move * speed * Time.deltaTime;
    }
}
