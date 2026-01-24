using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Linq;

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

    // --- CSV KAYIT DEÐÝÞKENLERÝ ---
    private string csvFilePath;
    private int trackedEpisodeIndex = 0;

    // Ajan resetlendiðinde veriler 0 olduðu için, son karedeki veriyi burada tutacaðýz
    private float tempLastReward = 0;
    private int tempLastSteps = 0;
    private float tempLastEpsilon = 0;

    void Start()
    {
        // 1. Referanslarý Bul
        if (!agentScript) agentScript = FindObjectOfType<AjanRL>();
        if (!targetScript) targetScript = FindObjectOfType<CurriculumTarget>();
        if (!envScript) envScript = FindObjectOfType<QBombENV_sc>();

        // 2. Klasör ve Dosya Yolunu Ayarla
        string folderPath = Path.Combine(Application.dataPath, "Analysis");
        if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

        // Dosya ismine tarih ekle ki eskileri silinmesin
        string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        csvFilePath = Path.Combine(folderPath, $"TrainingData_{timestamp}_Global2.csv");

        // 3. CSV Baþlýðýný Oluþtur
        if (!File.Exists(csvFilePath))
        {
            string header = "Episode;Steps;TotalReward;Epsilon";
            File.WriteAllText(csvFilePath, header + "\n");
            Debug.Log($"CSV Dosyasý Oluþturuldu: {csvFilePath}");
        }

        // Baþlangýç takibi
        if (agentScript) trackedEpisodeIndex = agentScript.currentEpisodeIndex;
    }

    void Update()
    {
        if (agentScript == null) return;

        // --- UI GÜNCELLEME (Ekranda anlýk veriyi göster) ---
        if (episodeText) episodeText.text = $"Episode: {agentScript.currentEpisodeIndex} / {agentScript.maxEpisode}";
        if (phaseText && targetScript) phaseText.text = $"Phase: {targetScript.currentPhase}";
        if (totalRewardText) totalRewardText.text = $"Reward: {agentScript.currentEpisodeReward:F1}";
        if (stepsText) stepsText.text = $"Steps: {agentScript.currentEpisodeSteps}";
        if (epsilonText) epsilonText.text = $"Epsilon: {agentScript.epsilon:F3}";

        // --- CSV KAYIT MANTIÐI ---
        CheckAndLogToCSV();
    }

    void CheckAndLogToCSV()
    {
        // Eðer ajanýn bölüm sayýsý, bizim takip ettiðimizden büyükse; yeni bölüm baþlamýþ demektir.
        // Bu durumda BÝR ÖNCEKÝ bölümün verilerini (temp deðiþkenlerdeki) kaydederiz.
        if (agentScript.currentEpisodeIndex > trackedEpisodeIndex)
        {
            // Ýlk bölümden sonra kayýt baþlasýn
            if (trackedEpisodeIndex > 0)
            {
                LogEpisodeData(trackedEpisodeIndex, tempLastSteps, tempLastReward, tempLastEpsilon);
            }

            // Sayacý güncelle
            trackedEpisodeIndex = agentScript.currentEpisodeIndex;
        }

        // --- VERÝ ÖNBELLEKLEME (Caching) ---
        // Ajan yeni bölüme geçtiði an "currentEpisodeReward" 0 olur.
        // Bu yüzden her karede, eldeki veriyi "temp" deðiþkenlere atýyoruz.
        // Bölüm deðiþtiði an elimizde kalan son veri, o bölümün final verisi olur.
        tempLastReward = agentScript.currentEpisodeReward;
        tempLastSteps = agentScript.currentEpisodeSteps;
        tempLastEpsilon = agentScript.epsilon;
    }

    void LogEpisodeData(int episode, int steps, float reward, float epsilon)
    {
        // Veriyi CSV formatýnda hazýrla
        // F2: Virgülden sonra 2 basamak, F4: 4 basamak
        string line = $"{episode};{steps};{reward:F2};{epsilon:F4}";

        // Dosyanýn sonuna ekle (Append)
        try
        {
            File.AppendAllText(csvFilePath, line + "\n");
        }
        catch (System.Exception e)
        {
            Debug.LogError("CSV Yazma Hatasý: " + e.Message);
        }
    }
}