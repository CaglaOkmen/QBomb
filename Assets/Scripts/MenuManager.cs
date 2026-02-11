using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuManager : MonoBehaviour
{
    [Header("Sahne Ýsimleri")]
    public string trainSceneName = "Train";
    public string gameSceneName = "Game";

    public void OnTrainButtonClick()
    {
        SceneManager.LoadScene(trainSceneName);
    }

    public void OnGameButtonClick()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}