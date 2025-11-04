using System.Collections.Generic;
using UnityEngine;

public class RobotManager : MonoBehaviour
{
    private GameManager gm;
    //private Tile[,] grid;
    private List<Tile[,]> RTgrid; //Real time grid
    public GameObject robotPrefab;

    public void Init(GameManager manager) => gm = manager;

    public void Start()
    {
        //grid = gm.gridManager.grid;
        RTgrid = gm.gridManager.RTgrid;
        foreach (var tile in RTgrid[0])
        {
            if (tile.flags == TileFlags.Spawn)
            {
                Vector3 spawnPos = new Vector3(
                    tile.x * gm.gridManager.cellSize + gm.gridManager.cellSize / 2,
                    0.5f,
                    tile.y * gm.gridManager.cellSize + gm.gridManager.cellSize / 2
                );
                Instantiate(robotPrefab, spawnPos, Quaternion.identity);
            }
        }

    }


    public void OnRobotClicked(GameObject robot)
    {
        // Na razie tylko log
        Debug.Log("[RobotManager] Wybrano robota: " + robot.name);

        // tutaj w przysz³oœci mo¿esz dodaæ np. otwarcie panelu UI albo komendê "jedŸ do celu"
    }
}

public class Robot : MonoBehaviour
{
    public float speed = 1f;
    private Vector3 targetPosition;


    void Start()
    {
        targetPosition = transform.position;
    }
    void Update()
    {
        if (Vector3.Distance(transform.position, targetPosition) > 0.1f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, speed * Time.deltaTime);
        }
    }
    public void MoveTo(Vector3 position)
    {
        targetPosition = position;
    }
}
