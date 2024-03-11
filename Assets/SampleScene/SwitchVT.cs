using UnityEngine;
using UnityEngine.UI;

public class SwitchVT : MonoBehaviour
{
    public GameObject vtGameObject;
    public Button button;
    public Text text;

    public void Start()
    {
        Application.targetFrameRate = -1;
        button.onClick.AddListener(delegate
        {
            vtGameObject.SetActive(!vtGameObject.activeSelf);
            text.text = $"VT: {vtGameObject.activeSelf}";
            Application.targetFrameRate = -1;
        });
        text.text = $"VT: {vtGameObject.activeSelf}";
    }
}