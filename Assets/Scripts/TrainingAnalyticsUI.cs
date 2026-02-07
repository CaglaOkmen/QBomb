using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

#if !UNITY_WEBGL || UNITY_EDITOR
using System.IO;
#endif

public class TrainingAnalyticsUI : MonoBehaviour
{
    [Header("References")]
    public AjanRL agentScript;
    public CurriculumTarget targetScript;
    public QBombENV_sc envScript;

    [Header("UI Text Fields")]
    public Text episodeText;
    public Text phaseText;
    public Text totalRewardText;
    public Text stepsText;
    public Text epsilonText;

    // CSV Kayit
    private string csvFilePath;
    private int trackedEpisodeIndex = 0;

    private float tempLastReward = 0;
    private int tempLastSteps = 0;
    private float tempLastEpsilon = 0;

    private int lastWallsBroken = 0;
    private int lastWallsInitial = 0;

    void Start()
    {
        if (!agentScript) agentScript = FindObjectOfType<AjanRL>();
        if (!targetScript) targetScript = FindObjectOfType<CurriculumTarget>();
        if (!envScript) envScript = FindObjectOfType<QBombENV_sc>();

        // Dosya ismi dinamik belirlenecek
        if (agentScript) trackedEpisodeIndex = agentScript.currentEpisodeIndex;
    }

    public void ResetTracking()
    {
        trackedEpisodeIndex = 0;
        tempLastReward = 0;
        tempLastSteps = 0;
        Debug.Log("Analiz takibi sifirlandi.");
    }
    public void SetCSVFileName(string fileName)
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        string folderPath = Path.Combine(Application.dataPath, "Analysis");
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        csvFilePath = Path.Combine(folderPath, fileName);

        if (!File.Exists(csvFilePath))
        {
            string header = "Episode;Outcome;DeathType;Phase;Steps;TotalReward;Epsilon;" +
                            "ExplorationRate;WallsInitial;WallsBroken;BombsDropped;SafetyDangerSteps";
            File.WriteAllText(csvFilePath, header + "\n");
        }
        Debug.Log($"CSV Log Hedefi: {fileName}");
#else
        Debug.Log("WebGL Mode: CSV writing disabled for " + fileName);
#endif
    }

    void Update()
    {
        if (agentScript == null) return;

        if (episodeText) episodeText.text = $"Episode: {agentScript.currentEpisodeIndex} / {agentScript.maxEpisode}";
        if (phaseText && targetScript) phaseText.text = $"Phase: {targetScript.currentPhase}";
        if (totalRewardText) totalRewardText.text = $"Reward: {agentScript.currentEpisodeReward:F1}";
        if (stepsText) stepsText.text = $"Steps: {agentScript.currentEpisodeSteps}";
        if (epsilonText) epsilonText.text = $"Epsilon: {agentScript.epsilon:F3}";

        CheckAndLogToCSV();
    }

    void CheckAndLogToCSV()
    {
        if (agentScript.currentEpisodeIndex > trackedEpisodeIndex)
        {
            if (trackedEpisodeIndex > 0)
            {
                LogEpisodeData(trackedEpisodeIndex);
            }
            trackedEpisodeIndex = agentScript.currentEpisodeIndex;
        }

        tempLastReward = agentScript.currentEpisodeReward;
        tempLastSteps = agentScript.currentEpisodeSteps;
        tempLastEpsilon = agentScript.epsilon;

        if (envScript != null)
        {
            lastWallsBroken = envScript.wallsBroken;
            lastWallsInitial = envScript.totalBreakableWalls;
        }
    }

    public void LogEpisodeData(int episode)
    {
        // WebGL de dosya yazma islemi yapilmaz
#if !UNITY_WEBGL || UNITY_EDITOR
        if (string.IsNullOrEmpty(csvFilePath)) return;

        string outcome = agentScript.lastOutcome;
        string deathType = agentScript.lastDeathType;
        int phase = (targetScript != null) ? targetScript.currentPhase : 0;

        int steps = tempLastSteps;
        float reward = tempLastReward;
        float epsilon = tempLastEpsilon;
        float expRate = agentScript.lastExplorationRate;

        int wInit = lastWallsInitial;
        int wBroken = lastWallsBroken;
        int bombs = agentScript.lastBombsDropped;
        int dangerSteps = agentScript.lastSafetyDangerSteps;

        string line = $"{episode};{outcome};{deathType};{phase};{steps};{reward:F2};{epsilon:F4};" +
                      $"{expRate:F2};{wInit};{wBroken};{bombs};{dangerSteps}";

        try
        {
            File.AppendAllText(csvFilePath, line + "\n");
        }
        catch (System.Exception e)
        {
            Debug.LogError("CSV Yazma Hatasý: " + e.Message);
        }
#endif
    }
}