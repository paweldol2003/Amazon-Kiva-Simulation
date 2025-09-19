using UnityEngine;
using UnityEngine.InputSystem;

public class SelectionManager : MonoBehaviour
{
    private GameManager gm;
    public LayerMask selectableMask; // przypisz w Inspectorze np. "Robot"

    public void Init(GameManager manager) => gm = manager;

    void Update()
    {
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            TrySelect();
        }
    }

    void TrySelect()
    {
        Camera activeCam = gm.cameraManager.GetActiveCamera();
        if (!activeCam) return;

        Ray ray = activeCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, selectableMask))
        {
            GameObject hitObj = hit.collider.gameObject;

            if (hitObj.CompareTag("Robot"))
            {
                Debug.Log("Klikniêto robota PPM: " + hitObj.name);
                gm.robotManager?.OnRobotClicked(hitObj);
            }
        }
    }
}
