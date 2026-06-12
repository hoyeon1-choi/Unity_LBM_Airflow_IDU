using UnityEngine;

public class LookAt : MonoBehaviour
{
    [SerializeField] private Vector3 lookAtVector;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        transform.LookAt(lookAtVector);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
