using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Desktop stand-in for the VR walk: WASD to move on the ground plane, hold right
/// mouse to look. This is the ONLY input-aware component in the walk pipeline; the
/// VR port replaces it with the XR rig and nothing downstream changes.
/// Uses the new Input System (project's activeInputHandler = Input System Package).
/// </summary>
public class DesktopWalker : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1.5f;
    [SerializeField] private float eyeHeight = 1.6f;
    [SerializeField] private bool lockEyeHeight = true;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.1f;
    [SerializeField] private bool requireRightMouseToLook = true;

    private float _yaw;
    private float _pitch;

    private void OnEnable()
    {
        Vector3 e = transform.eulerAngles;
        _yaw = e.y;
        _pitch = NormalizePitch(e.x);
    }

    private void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        UpdateLook();
        UpdateMove(kb);
    }

    private void UpdateLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;
        if (requireRightMouseToLook && !mouse.rightButton.isPressed) return;

        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;
        _yaw += delta.x;
        _pitch = Mathf.Clamp(_pitch - delta.y, -89f, 89f);
        transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    private void UpdateMove(Keyboard kb)
    {
        Vector3 input = Vector3.zero;
        if (kb.wKey.isPressed) input.z += 1f;
        if (kb.sKey.isPressed) input.z -= 1f;
        if (kb.dKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed) input.x -= 1f;
        if (input.sqrMagnitude < 0.0001f) return;

        // Move relative to where we're facing, but flattened to the ground.
        Vector3 move = Quaternion.Euler(0f, _yaw, 0f) * input.normalized;
        Vector3 pos = transform.position + move * (moveSpeed * Time.deltaTime);
        if (lockEyeHeight) pos.y = eyeHeight;
        transform.position = pos;
    }

    private static float NormalizePitch(float x) => x > 180f ? x - 360f : x;
}
