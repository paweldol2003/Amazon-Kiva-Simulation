using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class AutomationManager : MonoBehaviour
{
    private GameManager gm;
    public void Init(GameManager gm) => this.gm = gm;

    [Header("Keybinds")]
    public Key modeSwitch = Key.M;
    public Key randomPath = Key.F;
    public Key nextStep = Key.RightArrow;

    public int currentStep = 0;
    private List<Tile[,]> RTgrid; //Real time grid

    void Start()
    {
        RTgrid = gm.gridManager.RTgrid;
    }

    // Update is called once per frame
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[randomPath].wasPressedThisFrame)
            gm.pathManager.SetRandomPath(currentStep);

        if (kb[nextStep].wasPressedThisFrame) 
        {
            //gm.pathManager.ExecuteStep(currentStep);
            currentStep++;
            gm.gridManager.UpdateRTgrid(currentStep);

            Debug.Log("[AutomationManager] Aktualny step: " + (currentStep));
            Debug.Log($"Iloœæ gridów:  {RTgrid.Count}");

        }
    }


}
