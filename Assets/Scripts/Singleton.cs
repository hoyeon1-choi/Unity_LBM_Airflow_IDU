using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                // Find in scene first
                _instance = FindFirstObjectByType<T>();

                // If nothing, create new one
                if (_instance == null)
                {
                    GameObject obj = new GameObject(typeof(T).Name);
                    _instance = obj.AddComponent<T>();
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // Force to make sure this is the only instance
        if (_instance != null && _instance != this as T)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this as T;

        if (transform.parent != null)
        {
            transform.SetParent(null);
        }

        DontDestroyOnLoad(gameObject);
    }
}
