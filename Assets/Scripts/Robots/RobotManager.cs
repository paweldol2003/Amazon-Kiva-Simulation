using UnityEngine;

public class RobotManager : MonoBehaviour
{
    private GameManager gm;

    public void Init(GameManager manager) => gm = manager;

    public void OnRobotClicked(GameObject robot)
    {
        // Na razie tylko log
        Debug.Log("[RobotManager] Wybrano robota: " + robot.name);

        // tutaj w przysz³oœci mo¿esz dodaæ np. otwarcie panelu UI albo komendê "jedŸ do celu"
    }
}
