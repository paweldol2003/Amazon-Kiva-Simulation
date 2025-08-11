using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class GridMeshOverlayTiles : MonoBehaviour
{
    public GridModel grid;
    public Material unlitMaterial;   // przypnij Unlit/Color
    Mesh mesh; Color[] colors;

    void Start()
    {
        if (!grid) grid = FindFirstObjectByType<GridModel>();
        mesh = new Mesh { name = "GridMeshOverlayTiles" };
        GetComponent<MeshFilter>().mesh = mesh;
        if (unlitMaterial) GetComponent<MeshRenderer>().material = unlitMaterial;
        BuildGeometry();
        RecolorAll();
    }

    void BuildGeometry()
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        colors = new Color[grid.cfg.width * grid.cfg.length * 4];
        int vi = 0;

        for (int y = 0; y < grid.cfg.length; y++)
            for (int x = 0; x < grid.cfg.width; x++)
            {
                var c = grid.CellCenter(x, y); float h = grid.cfg.cellSize * 0.5f;
                verts.Add(new Vector3(c.x - h, 0, c.y - h));
                verts.Add(new Vector3(c.x - h, 0, c.y + h));
                verts.Add(new Vector3(c.x + h, 0, c.y + h));
                verts.Add(new Vector3(c.x + h, 0, c.y - h));

                tris.Add(vi); tris.Add(vi + 1); tris.Add(vi + 2);
                tris.Add(vi); tris.Add(vi + 2); tris.Add(vi + 3);
                vi += 4;
            }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    Color ColorOf(Tile t) => (t.flags & TileFlags.Blocked) != 0 ? Color.black : (Color)t.color;

    void SetCellColor(int x, int y, Color c)
    {
        int baseIdx = (y * grid.cfg.width + x) * 4;
        colors[baseIdx] = colors[baseIdx + 1] = colors[baseIdx + 2] = colors[baseIdx + 3] = c;
    }

    public void RefreshCell(int x, int y)
    {
        SetCellColor(x, y, ColorOf(grid.tiles[x, y]));
        mesh.colors = colors;
    }

    public void RecolorAll()
    {
        for (int y = 0; y < grid.cfg.length; y++)
            for (int x = 0; x < grid.cfg.width; x++)
                SetCellColor(x, y, ColorOf(grid.tiles[x, y]));
        mesh.colors = colors;
    }
}
