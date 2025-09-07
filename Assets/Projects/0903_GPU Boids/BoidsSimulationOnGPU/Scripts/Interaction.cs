using UnityEngine;
using UnityEngine.InputSystem;

public class Interactin : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        var mousePos  = Mouse.current.position.ReadValue();
        var screenPos = new Vector3(mousePos.x, mousePos.y, 20.0f);
        var worldPos  = Camera.main.ScreenToWorldPoint(screenPos);

        transform.localPosition = worldPos;
    }
}
