using UnityEngine;

public class CyberpunkCamera : MonoBehaviour
{
    [Header("Look Sensitivity")]
    public float mouseSensitivity = 2f;
    
    [Header("Look Restrictions")]
    public float minXLook = -80f;
    public float maxXLook = 80f;

    [Header("Cyberpunk Roll Sway")]
    public float swayAmount = 1.5f;
    public float swaySpeed = 5f;

    private Transform characterBody;
    private float xRotation = 0f;

    void Start()
    {
        // Lock cursor to the middle of the screen
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Automatically find the root character object (assumes script is deep inside bones)
        characterBody = transform.root;
    }

    void LateUpdate()
    {
        // 1. Get raw mouse inputs
        float mouseX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

        // 2. Vertical Look (Look Up/Down)
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, minXLook, maxXLook);

        // 3. Cyberpunk Rotational Sway (Rolls camera slightly when turning mouse)
        float targetRoll = -mouseX * swayAmount;
        // Smoothly return the roll back to 0 or lean into the turn
        float currentRoll = Mathf.Lerp(0, targetRoll, Time.deltaTime * swaySpeed);

        // 4. Apply Rotations
        // Local rotation controls looking up/down and the stylistic roll sway
        transform.localRotation = Quaternion.Euler(xRotation, 0f, currentRoll);
        
        // Horizontal rotation turns the actual character body in the world
        characterBody.Rotate(Vector3.up * mouseX);
    }
}