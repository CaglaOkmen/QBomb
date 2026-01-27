using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class ExperimentManager : MonoBehaviour
{
    [Header("References")]
    public AjanRL agentScript;
    public CurriculumTarget targetScript;
    public TrainingAnalyticsUI uiScript;
    public Text experimentInfoText;

    public enum ExperimentType
    {
        Curriculum_Safety,
        Curriculum_NoSafety,
        NoCurriculum_Safety,
        NoCurriculum_NoSafety
    }

    [Header("Current Status")]
    public ExperimentType currentExperiment = ExperimentType.Curriculum_Safety;

    private void Start()
    {
        ApplyExperimentSettings();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            SwitchToNextExperiment();
        }

        // 'T' tusuna basildiginda otomatik test dongusunu baslat
        if (Input.GetKeyDown(KeyCode.T))
        {
            StopAllCoroutines();
            StartCoroutine(RunFullTestSequence());
        }
    }

    // --- TEST SENARYOLARI DONGUSU ---
    IEnumerator RunFullTestSequence()
    {
        Debug.Log($"<color=orange>TEST BASLADI: {currentExperiment} modeli test ediliyor...</color>");

        // Model ismini uzantisiz al (CSV ismi icin)
        string modelBaseName = agentScript.currentSaveFileName.Replace(".json", "");

        // Senaryo Tanimlari: Faz, Agresiflik, BombaOlasiligi
        (int phase, float aggro, float bomb)[] scenarios = new (int, float, float)[]
        {
            (1, 0.0f, 0.0f), // Senaryo 1: Faz 1 (Hedef sabit)
            (2, 0.0f, 0.0f), // Senaryo 2: Faz 2 (Hedef rastgele)
            (3, 0.7f, 0.4f), // Senaryo 3: Faz 3 (Hedef Agresif)
            (3, 0.3f, 0.3f), // Senaryo 4: Faz 3 (dusuk Agresiflik)
            (3, 0.6f, 0.6f), // Senaryo 5: Faz 3 (orta Agresiflik)
            (3, 9.0f, 9.0f)  // Senaryo 6: Faz 3 (yuksek Agresiflik/Bomba)
        };

        for (int i = 0; i < scenarios.Length; i++)
        {
            var s = scenarios[i];

            // 1. Hedef Parametrelerini Ayarla
            targetScript.currentPhase = s.phase;
            targetScript.aggressionLevel = s.aggro;
            targetScript.bombNearAgent = s.bomb;
            targetScript.bombNearWall = s.bomb; // Duvar yaninda bomba koyma da paralel artsin

            // 2. CSV Dosyasini Hazirla
            string scenarioName = $"Test_{modelBaseName}_Scen{i + 1}_Ph{s.phase}_Ag{s.aggro}";
            uiScript.SetCSVFileName(scenarioName + ".csv");
            uiScript.ResetTracking();

            if (experimentInfoText)
                experimentInfoText.text = $"TESTING Scen {i + 1}/6\nModel: {modelBaseName}\nPhase: {s.phase}\nAggro: {s.aggro}";

            // 3. 30 Episode Testi Kos
            yield return StartCoroutine(agentScript.AjanTestingLoop(30));

            Debug.Log($"<color=green>Senaryo {i + 1} Tamamlandi: {scenarioName}</color>");
        }

        Debug.Log("<color=cyan>TUM TEST SENARYOLARI BITTI!</color>");
        if (experimentInfoText) experimentInfoText.text = "TEST COMPLETED!";
    }

    void SwitchToNextExperiment()
    {
        int current = (int)currentExperiment;
        current = (current + 1) % 4;
        currentExperiment = (ExperimentType)current;

        ApplyExperimentSettings();

        agentScript.StopAllCoroutines();
        FindObjectOfType<QBombENV_sc>().Reset();

        Debug.Log($"<color=cyan>EXPERIMENT SWITCHED TO: {currentExperiment}</color>");
    }

    void ApplyExperimentSettings()
    {
        string modelName = "";
        bool safety = false;
        bool useCurriculum = false;

        switch (currentExperiment)
        {
            case ExperimentType.Curriculum_Safety:
                modelName = "model_cur_safe.json";
                safety = true;
                useCurriculum = true;
                break;
            case ExperimentType.Curriculum_NoSafety:
                modelName = "model_cur_nosafe.json";
                safety = false;
                useCurriculum = true;
                break;
            case ExperimentType.NoCurriculum_Safety:
                modelName = "model_nocur_safe.json";
                safety = true;
                useCurriculum = false;
                break;
            case ExperimentType.NoCurriculum_NoSafety:
                modelName = "model_nocur_nosafe.json";
                safety = false;
                useCurriculum = false;
                break;
        }

        agentScript.SetExperimentConfig(modelName, safety);
        targetScript.SetCurriculumMode(useCurriculum);

        if (uiScript != null)
            uiScript.SetCSVFileName($"Data_{currentExperiment}.csv");

        if (experimentInfoText)
            experimentInfoText.text = $"EXP: {currentExperiment}\nSafety: {safety}\nCurriculum: {useCurriculum}";
    }
}