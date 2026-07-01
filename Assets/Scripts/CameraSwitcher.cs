using UnityEngine;
using Unity.Cinemachine;

public class CameraSwitcher : MonoBehaviour
{
    public CinemachineCamera freeLookCam;
    public CinemachineCamera combatCam;

    void Update()
    {
        if (Input.GetMouseButton(1))
        {
            combatCam.Priority = 20;
            freeLookCam.Priority = 10;
        }
        else
        {
            combatCam.Priority = 5;
            freeLookCam.Priority = 20;
        }
    }
}