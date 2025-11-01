using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    private GameManager gm;
    public void Init(GameManager gm) => this.gm = gm;

    [Header("References")]
    [SerializeField] private TextMeshProUGUI stepLabel;
    [SerializeField] private TextMeshProUGUI iterationLabel;

    private void Awake()
    {

    }

    public void UpdateStep(int current, int max)
    {
        if (stepLabel)
            stepLabel.text = max > 0 ? $"Step: {current}/{max}" : $"Step: {current}";
    }

    public void UpdateIteration(int current, int total)
    {
        if (iterationLabel)
            iterationLabel.text = total > 0 ? $"Iteration: {current}/{total}" : $"Iteration: {current}";
    }

    public void ShowMessage(string msg)
    {
        Debug.Log("[UI] " + msg);
        // tu mo¿esz dodaæ np. TMP popup albo panel komunikatów
    }
}
