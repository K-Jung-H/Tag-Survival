using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public sealed class StorySelectBootstrap : MonoBehaviour
{
    [SerializeField] private StorySelectSceneController controller;

    private void Awake()
    {
        EnsureCamera();
        EnsureEventSystem();
        if (controller != null)
        {
            controller.Initialize();
        }
    }

    private void EnsureCamera()
    {
        if (Camera.main != null || FindFirstObjectByType<Camera>() != null)
        {
            return;
        }

        GameObject cameraObject = new("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.backgroundColor = new Color(0.06f, 0.07f, 0.08f, 1f);

        if (FindFirstObjectByType<AudioListener>() == null)
        {
            cameraObject.AddComponent<AudioListener>();
        }
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}
