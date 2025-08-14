// Assets/Scripts/Cameras/StaticOverheadCamera.cs
using UnityEngine;

public class TopCamera : MonoBehaviour
{
    public GridModel grid;       // auto-kadr na siatkê
    public float padding = 0.1f; // 10% marginesu wokó³ siatki
    public float yOffset = 10f;

    void Start()
    {
        var cam = GetComponent<Camera>();
        cam.orthographic = true;

        if (grid != null)
        {
            float w = grid.cfg.width * grid.cfg.cellSize;
            float h = grid.cfg.length * grid.cfg.cellSize;
            Vector3 center = new(
                grid.cfg.origin.x + w * 0.5f,
                yOffset,
                grid.cfg.origin.y + h * 0.5f

            );

            transform.position = new Vector3(center.x, center.y, center.z);
            transform.rotation = Quaternion.Euler(90,0,0);

            float aspect = cam.aspect > 0 ? cam.aspect : (float)Screen.width / Screen.height;
            float needed = 0.5f * Mathf.Max(h, w / aspect);
            cam.orthographicSize = needed * (1f + padding);
        }
    }
}
