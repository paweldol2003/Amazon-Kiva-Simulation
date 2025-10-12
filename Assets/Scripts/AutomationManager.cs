using UnityEngine;
using UnityEngine.InputSystem;

public class AutomationManager : MonoBehaviour
{
    private GameManager gm;
    public void Init(GameManager gm) => this.gm = gm;

    [Header("Keybinds")]
    public Key modeSwitch = Key.M;
    public Key randomPath = Key.F;

    
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb[randomPath].wasPressedThisFrame)
            gm.pathManager.SetRandomPath();
    }


}
