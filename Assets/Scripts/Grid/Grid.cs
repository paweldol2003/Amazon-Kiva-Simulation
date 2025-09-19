using UnityEngine;

public class GridRandomColors : MonoBehaviour
{
    [Header("Grid")]
    public int width = 10;
    public int length = 10;
    public float cellSize = 1f;
    public Vector2 origin = Vector2.zero;

    [Header("Kolory")]
    public int randomSeed = 0;       // 0 = losowo za ka¿dym razem; >0 = powtarzalnie

    Sprite _sprite;

    void Awake()
    {
        BuildSharedSprite();   // 1×1 sprite o rzeczywistej wielkoœci 1u
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        // wyczyœæ stare dzieci
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        if (randomSeed != 0) Random.InitState(randomSeed);

        for (int z = 0; z < length; z++)
            for (int x = 0; x < width; x++)
            {
                var go = new GameObject($"Cell_{x}_{z}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(
                    origin.x + (x + 0.5f) * cellSize,
                    0f,
                    origin.y + (z + 0.5f) * cellSize
                );

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.drawMode = SpriteDrawMode.Sliced; // bez skalowania tekstury
                sr.size = new Vector2(cellSize, cellSize);

                // losowy kolor
                sr.color = new Color(Random.value, Random.value, Random.value, 1f);
            }
    }

    void BuildSharedSprite()
    {
        if (_sprite != null) return;
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f); // PPU=1 => 1px = 1 unit
        _sprite.name = "UnitWhiteSprite";
    }
}
