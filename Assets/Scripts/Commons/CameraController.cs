using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    private Camera _camera;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        // _camera.orthographic = true;
        // transform.position = new Vector3(0f, 7f, 0f);
        // transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        // _camera.orthographic = true;
        // transform.position = new Vector3(-5f, 1.25f, 0f);
        // transform.rotation = Quaternion.Euler(0f, 90f, 0f);
    }

    // Update is called once per frame
    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame)
        {
            _camera.orthographic = true;
            transform.position = new Vector3(0f, 7f, 0f);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            Debug.Log("Camera position reset to top-down view.");
        }

        if (kb.digit2Key.wasPressedThisFrame)
        {
            _camera.orthographic = true;
            transform.position = new Vector3(-5f, 1.25f, 0f);
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            Debug.Log("Camera position reset to side view.");
        }

        if (kb.digit3Key.wasPressedThisFrame)
        {
            _camera.orthographic = false;
            transform.position = new Vector3(-4f, 5f, -4f);
            transform.rotation = Quaternion.Euler(40f, 45f, 0f);
            Debug.Log("Camera position reset to perspective view.");
        }
    }
}
