using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

// WebGL haricinde IO kutuphanesini kullan
#if !UNITY_WEBGL || UNITY_EDITOR
using System.IO;
#endif

public class AjanRL : MonoBehaviour
{
    [Header("Hiperparametreler")]
    public int maxEpisode = 4000;
    public float learningRate = 0.0001f;
    public float gamma = 0.99f;
    public float epsilon = 1.0f;
    public float epsilonDecay = 0.9991f;
    public float minEpsilon = 0.05f;

    [Header("MLP")]
    public int hiddenLayerSize = 64;
    public int batchSize = 32;

    [Header("Train modu")]
    public bool isTraining = false;

    public string currentSaveFileName = "default_model.json";

    [Header("Guvenlik katmaný")]
    public bool useSafetyRule = true;

    [Header("Gorselleme")]
    public float currentEpisodeReward = 0;
    public int currentEpisodeSteps = 0;
    public int currentEpisodeIndex = 0;

    // --- CSV raporlama icin
    [Header("Analiz Verileri (Last Episode)")]
    public string lastOutcome = "None";
    public string lastDeathType = "None";
    public float lastExplorationRate = 0f;
    public int lastBombsDropped = 0;
    public int lastSafetyDangerSteps = 0;

    // Epsilon ve Faz Yonetimi
    private int lastPhaseTracker = -1;
    private CurriculumTarget targetScript;

    // 13 Hucre 
    private readonly Vector2Int[] observationPattern = new Vector2Int[]
    {
        // Mesafe 0 (Merkez)
        new Vector2Int(0, 0),

        // Mesafe 1 (3x3 Karenin geri kalani - 8 hucre)
        new Vector2Int(0, 1),   // Yukari
        new Vector2Int(0, -1),  // Asagi
        new Vector2Int(1, 0),   // Sag
        new Vector2Int(-1, 0),  // Sol
        new Vector2Int(1, 1),   // Sag-Ust
        new Vector2Int(-1, 1),  // Sol-Ust
        new Vector2Int(1, -1),  // Sag-Alt
        new Vector2Int(-1, -1), // Sol-Alt

        // Mesafe 2 (Sadece Arti Sekli - 4 hucre)
        new Vector2Int(0, 2),   // Yukari 2
        new Vector2Int(0, -2),  // Asagi 2
        new Vector2Int(2, 0),   // Sag 2
        new Vector2Int(-2, 0)   // Sol 2
    };

    private int inputSize;

    private SimpleMLP mlp;
    private ReplayBuffer replayBuffer;
    private QBombENV_sc env;
    private Pathfinder pathfinder;

    // Ýstatistikler
    private int winCount = 0;
    private int deathCount = 0;
    private int timeoutCount = 0;

    private int[] actionCounts;

    public void SetExperimentConfig(string fileName, bool safetyEnabled)
    {
        currentSaveFileName = fileName;
        useSafetyRule = safetyEnabled;

        // kayitli modeli yukle, yoksa sifir ag baslat
        LoadNetwork();
    }

    void Awake()
    {
        // 6 kanal (IsWall, IsBreakable, IsTarget, IsBomb, BombTimer, IsInDanger)
        int gridCells = observationPattern.Length;
        int channelsPerCell = 6;
        int gridInputs = gridCells * channelsPerCell;

        // Global inputs: Target X,Y, Targete uzaklýk, tehlikede mi, Ajan ve Target bombalai aktif mi, Yol acik kapali
        int globalInputs = 5;

        inputSize = gridInputs + globalInputs;

        env = FindObjectOfType<QBombENV_sc>();
        pathfinder = gameObject.AddComponent<Pathfinder>();
        targetScript = FindObjectOfType<CurriculumTarget>();
    }

    void Start()
    {
        Time.timeScale = 25f;

        actionCounts = new int[env.numActions];
        mlp = new SimpleMLP(inputSize, hiddenLayerSize, env.numActions, learningRate);
        replayBuffer = new ReplayBuffer(10000);

        if (!isTraining) LoadNetwork();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) { StopAllCoroutines(); StartCoroutine(AjanTraining()); }
        if (Input.GetKeyDown(KeyCode.K)) SaveNetwork();
    }

    float[] GetObservation()
    {
        float[] observations = new float[inputSize];
        int index = 0;
        float cellSize = env.cellSize;
        Vector2 agentPos = transform.position;

        // 1 birim tam cevre ve 2 birim uzaklik + seklindeki alanlar
        foreach (Vector2Int offset in observationPattern)
        {
            Vector2 scanPos = agentPos + new Vector2(offset.x * cellSize, offset.y * cellSize);

            bool isWall = false;
            bool isBreakable = false;
            bool isTarget = false;
            bool isBomb = false;
            float bombTimer = 0f; // 0: Yok, 0-1 arasi: patlama suresi
            bool isDangerous = false;

            Collider2D hit = Physics2D.OverlapBox(scanPos, Vector2.one * (cellSize * 0.9f), 0);
            if (hit != null)
            {
                if (hit.CompareTag("wall")) isWall = true;
                else if (hit.CompareTag("breakable")) isBreakable = true;
                else if (hit.CompareTag("target")) isTarget = true;
                else if (hit.CompareTag("bomb"))
                {
                    isBomb = true;
                    // Bombanin patlamasina ne kadar kaldi
                    SimpleBomb sb = hit.GetComponent<SimpleBomb>();

                    // +1 ekleyerek 0 olmamasini sagliyoruz (0 bomba yok demek)
                    // Ornegin 3 adimli bombada: 1/4, 2/4, 3/4, 4/4 gibi degerler alir
                    bombTimer = (float)(sb.currentStep + 1) / (sb.explosionSteps + 1);
                }
            }

            int checkX = Mathf.RoundToInt(scanPos.x / cellSize);
            int checkY = Mathf.RoundToInt(scanPos.y / cellSize);

            if (checkX >= 0 && checkX < env.width && checkY >= 0 && checkY < env.height)
            {
                isDangerous = env.dangerMap[checkX, checkY];
            }

            // Onehot kodlama ve Bomba zamani
            observations[index++] = isWall ? 1.0f : 0.0f;
            observations[index++] = isBreakable ? 1.0f : 0.0f;
            observations[index++] = isTarget ? 1.0f : 0.0f;
            observations[index++] = isBomb ? 1.0f : 0.0f;
            observations[index++] = bombTimer;
            observations[index++] = isDangerous ? 1.0f : 0.0f;

        }

        // hedefin yonu
        float relativeTargetX = (env.targetX - env.gridX) / (float)env.width;
        float relativeTargetY = (env.targetY - env.gridY) / (float)env.height;
        observations[index++] = relativeTargetX;
        observations[index++] = relativeTargetY;

        // hedefin mesafesi
        float manhattanDist = (Mathf.Abs(env.targetX - env.gridX) + Mathf.Abs(env.targetY - env.gridY));
        float normalizedDist = manhattanDist / (env.width + env.height);
        observations[index++] = normalizedDist;

        // Tehlike durumu
        bool inDanger = pathfinder.IsInDanger(env.gridX, env.gridY);
        observations[index++] = inDanger ? 1.0f : 0.0f;

        // Yol Acik, kapali
        observations[index++] = (pathfinder.currentPathType == Pathfinder.PathType.Clear) ? 1.0f : 0.0f;

        return observations;
    }

    IEnumerator AjanTraining()
    {
        int maxStepsPerEpisode = 600;

        winCount = 0; deathCount = 0; timeoutCount = 0;

        epsilon = 1.0f;
        lastPhaseTracker = -1; // Faz takibini sifirla

        print($"=== TRAINING START ({currentSaveFileName}) ===");
        isTraining = true;

        // Egitim baslarken UI sayacini temizle
        if (FindObjectOfType<TrainingAnalyticsUI>())
            FindObjectOfType<TrainingAnalyticsUI>().ResetTracking();

        for (int ep = 1; ep <= maxEpisode; ep++)
        {
            Random.InitState(ep);
            currentEpisodeIndex = ep;
            env.Reset();

            // Fazlara gore epsilon yonetimi
            if (targetScript != null)
            {
                if (targetScript.curriculumEnabled)
                {
                    int currentPhase = targetScript.currentPhase;

                    if (currentPhase != lastPhaseTracker)
                    {
                        if (currentPhase == 1)
                        {
                            // Faz 1: Hýzlý (1.0 -> 0.05, Decay 0.992)
                            epsilonDecay = 0.9925f;
                        }
                        else if (currentPhase == 2)
                        {
                            // Faz 2: Orta (Baslangic 0.8, Decay 0.995)
                            epsilon = 0.8f;
                            epsilonDecay = 0.997f;
                            print($"<color=yellow>PHASE 2 START: Epsilon reset to 0.8</color>");
                        }
                        else if (currentPhase == 3)
                        {
                            // Faz 3: Yavas (Baslangic 0.6, Decay 0.999)
                            epsilon = 0.6f;
                            epsilonDecay = 0.9991f;
                            print($"<color=red>PHASE 3 START: Epsilon reset to 0.6</color>");
                        }
                        lastPhaseTracker = currentPhase;
                    }
                }
                else
                {
                    // Mufredat Kapalý
                    if (lastPhaseTracker != 0)
                    {
                        epsilonDecay = 0.9992f;
                        lastPhaseTracker = 0;
                    }
                }
            }

            float[] state = GetObservation();
            bool done = false;
            float totalReward = 0;
            int steps = 0;

            currentEpisodeReward = 0;
            currentEpisodeSteps = 0;

            // Bolum istatistiklerini sifirlama
            System.Array.Clear(actionCounts, 0, actionCounts.Length);
            int episodeRandomActionCount = 0;
            int episodeBombs = 0;
            int episodeDangerSteps = 0;

            while (!done && steps < maxStepsPerEpisode)
            {
                int action = 0;
                float[] qValues = mlp.Forward(state);

                bool isRandomMove = false;
                // Karar mekanizmasý
                if (useSafetyRule && pathfinder.IsInDanger(env.gridX, env.gridY))
                {
                    int safeMove = pathfinder.GetSafeMove(env.gridX, env.gridY);
                    action = (safeMove != -1) ? safeMove : GetActionFromQ(qValues);
                }
                else
                {
                    if (Random.Range(0f, 1f) < epsilon)
                    {
                        action = GetRandomValidAction();
                        isRandomMove = true;
                    }
                    else
                    {
                        action = GetActionFromQ(qValues);
                    }
                }

                if (action < actionCounts.Length) actionCounts[action]++;
                if (isRandomMove) episodeRandomActionCount++;
                if (action == 4 && env.agentBombActive == false) episodeBombs++;
                if (pathfinder.IsInDanger(env.gridX, env.gridY)) episodeDangerSteps++;

                (float reward, bool terminated) = env.Step(action);
                done = terminated;

                float[] nextState = GetObservation();
                replayBuffer.Add(state, action, reward, nextState, done);

                if (replayBuffer.Count > batchSize)
                {
                    TrainNetwork();
                }

                state = nextState;
                totalReward += reward;
                steps++;

                currentEpisodeReward = totalReward;
                currentEpisodeSteps = steps;

                yield return null;
            }

            string resultReason = "TIMEOUT";
            string dType = "None";

            if (env.kill) { resultReason = "WIN"; winCount++; }
            else if (!env.isAlive)
            {
                resultReason = "DEATH";
                deathCount++;
                dType = env.deathType.ToString(); // Suicide veya KilledByTarget
            }
            else { timeoutCount++; }

            lastOutcome = resultReason;
            lastDeathType = dType;
            lastBombsDropped = episodeBombs;
            lastSafetyDangerSteps = episodeDangerSteps;
            lastExplorationRate = (steps > 0) ? (float)episodeRandomActionCount / steps : 0;

            if (epsilon > minEpsilon)
            {
                epsilon = Mathf.Max(minEpsilon, epsilon * epsilonDecay);
            }

            string actionReport = $"Actions => Y:{actionCounts[0]} A:{actionCounts[1]} S:{actionCounts[2]} Sol:{actionCounts[3]} B:{actionCounts[4]} WAIT:{actionCounts[5]}";

            print($"Ep: {ep} | {resultReason} | R: {totalReward:F1} | Eps: {epsilon:F3} (Decay:{epsilonDecay:F4}) | Ph:{targetScript?.currentPhase} | {actionReport}");

            if (ep % 20 == 0)
            {
                print($"20 Ep Durum: Wins:{winCount} Deaths:{deathCount} Timeouts:{timeoutCount}");
                winCount = 0; deathCount = 0; timeoutCount = 0;
                SaveNetwork();
            }
        }

        // Egitim bittiginde son episode'u zorla yazdir
        if (FindObjectOfType<TrainingAnalyticsUI>())
            FindObjectOfType<TrainingAnalyticsUI>().LogEpisodeData(currentEpisodeIndex);

        SaveNetwork();
        isTraining = false;
    }

    public IEnumerator AjanTestingLoop(int episodeCount)
    {
        isTraining = false;

        // Egitim baslarken UI sayacini temizle
        if (FindObjectOfType<TrainingAnalyticsUI>())
            FindObjectOfType<TrainingAnalyticsUI>().ResetTracking();

        float testEpsilon = 0f;

        for (int ep = 1; ep <= episodeCount; ep++)
        {
            currentEpisodeIndex = ep;
            env.Reset();

            bool done = false;
            int steps = 0;
            float totalReward = 0;
            int episodeBombs = 0;
            int episodeDangerSteps = 0;

            while (!done && steps < 400) // Test icin max step
            {
                float[] state = GetObservation();
                float[] qValues = mlp.Forward(state);
                int action = GetActionFromQ(qValues);

                // Guvenlik kurali aktifse ve tehlike varsa ez
                if (useSafetyRule && pathfinder.IsInDanger(env.gridX, env.gridY))
                {
                    int safeMove = pathfinder.GetSafeMove(env.gridX, env.gridY);
                    if (safeMove != -1) action = safeMove;
                }

                // Metrik takibi
                if (action == 4 && env.agentBombActive == false) episodeBombs++;
                if (pathfinder.IsInDanger(env.gridX, env.gridY)) episodeDangerSteps++;

                (float r, bool t) = env.Step(action);

                totalReward += r;
                done = t;
                steps++;

                currentEpisodeReward = totalReward;
                currentEpisodeSteps = steps;

                yield return null;
            }

            string resultReason = "TIMEOUT";
            string dType = "None";
            if (env.kill) resultReason = "WIN";
            else if (!env.isAlive) { resultReason = "DEATH"; dType = env.deathType.ToString(); }

            lastOutcome = resultReason;
            lastDeathType = dType;
            lastBombsDropped = episodeBombs;
            lastSafetyDangerSteps = episodeDangerSteps;
            lastExplorationRate = 0;

            yield return new WaitForSeconds(0.01f);
        }

        // Egitim bittiginde son episode'u yazdir
        if (FindObjectOfType<TrainingAnalyticsUI>())
            FindObjectOfType<TrainingAnalyticsUI>().LogEpisodeData(currentEpisodeIndex);
    }
    void TrainNetwork()
    {
        List<Experience> batch = replayBuffer.Sample(batchSize);
        foreach (var exp in batch)
        {
            float targetQ = exp.reward;
            if (!exp.done)
            {
                float[] nextQValues = mlp.Forward(exp.nextState);
                float maxQ = nextQValues.Max();

                // NaN kontrolü
                if (float.IsNaN(maxQ) || float.IsInfinity(maxQ))
                {
                    Debug.LogWarning("NaN in maxQ! Using 0.");
                    maxQ = 0f;
                }

                targetQ += gamma * maxQ;
            }

            targetQ = Mathf.Clamp(targetQ, -50f, 50f);

            float[] currentQValues = mlp.Forward(exp.state);
            float[] targetQVector = (float[])currentQValues.Clone();
            targetQVector[exp.action] = targetQ;
            mlp.Train(exp.state, targetQVector);
        }
    }

    int GetActionFromQ(float[] qValues)
    {
        // 1. Bomba maskeleme
        if (env.bombActive)
        {
            qValues[4] = float.NegativeInfinity;
        }
        bool up = IsValidMove(env.gridX, env.gridY + 1);
        bool down = IsValidMove(env.gridX, env.gridY - 1);
        bool right = IsValidMove(env.gridX + 1, env.gridY);
        bool left = IsValidMove(env.gridX - 1, env.gridY);

        // 2. Duvar maskeleme
        if (!up) qValues[0] = float.NegativeInfinity;
        if (!down) qValues[1] = float.NegativeInfinity;
        if (!right) qValues[2] = float.NegativeInfinity;
        if (!left) qValues[3] = float.NegativeInfinity;

        float maxVal = float.MinValue;
        int bestAction = -1;

        for (int i = 0; i < qValues.Length; i++)
        {
            if (qValues[i] > maxVal && qValues[i] != float.NegativeInfinity)
            {
                maxVal = qValues[i];
                bestAction = i;
            }
        }

        return bestAction;
    }

    int GetRandomValidAction()
    {
        List<int> validActions = new List<int>();

        bool up = IsValidMove(env.gridX, env.gridY + 1);
        bool down = IsValidMove(env.gridX, env.gridY - 1);
        bool right = IsValidMove(env.gridX + 1, env.gridY);
        bool left = IsValidMove(env.gridX - 1, env.gridY);

        if (up) validActions.Add(0);
        if (down) validActions.Add(1);
        if (right) validActions.Add(2);
        if (left) validActions.Add(3);

        if (!env.bombActive)
            validActions.Add(4);

        validActions.Add(5);

        if (validActions.Count > 0)
            return validActions[Random.Range(0, validActions.Count)];

        Debug.LogWarning("Geçerli action yok.");
        return -1;
    }

    bool IsValidMove(int x, int y)
    {
        if (x < 0 || x >= env.width || y < 0 || y >= env.height)
            return false;

        if (env.map[x, y] != 0)
            return false;

        return true;
    }

    void SaveNetwork()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        string path = Path.Combine(Application.persistentDataPath, currentSaveFileName);
        mlp.SaveModel(path);
#else
        // WebGL: Sadece dosya ismini (Key olarak) gonder
        mlp.SaveModel(currentSaveFileName);
#endif
    }

    void LoadNetwork()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        string path = Path.Combine(Application.persistentDataPath, currentSaveFileName);

        if (File.Exists(path))
        {
            mlp.LoadModel(path);
            print($"<color=green>Model Loaded: {currentSaveFileName}</color>");
        }
        else
        {
            mlp = new SimpleMLP(inputSize, hiddenLayerSize, env.numActions, learningRate);
            print($"<color=yellow>New Model Created: {currentSaveFileName}</color>");
        }
#else
        // WebGL: PlayerPrefs kontrolu
        if (PlayerPrefs.HasKey(currentSaveFileName))
        {
            mlp.LoadModel(currentSaveFileName);
            print($"<color=green>Model Loaded (PlayerPrefs): {currentSaveFileName}</color>");
        }
        else
        {
            mlp = new SimpleMLP(inputSize, hiddenLayerSize, env.numActions, learningRate);
            print($"<color=yellow>New Model Created (PlayerPrefs): {currentSaveFileName}</color>");
        }
#endif
    }

    private void OnDrawGizmos()
    {
        if (env == null) return;

        float cellSize = env.cellSize;
        Vector3 agentPos = Application.isPlaying ? transform.position : transform.position;

        Vector2Int[] currentPattern = observationPattern;
        if (currentPattern == null || currentPattern.Length == 0) return;

        foreach (Vector2Int offset in currentPattern)
        {
            Vector3 drawPos = agentPos + new Vector3(offset.x * cellSize, offset.y * cellSize, 0);

            float distanceFromCenter = Mathf.Sqrt(offset.x * offset.x + offset.y * offset.y);
            float alpha = 0.5f - (distanceFromCenter * 0.15f);
            if (alpha < 0.1f) alpha = 0.1f;

            Gizmos.color = new Color(1, 1, 1, alpha);
            Gizmos.DrawWireCube(drawPos, Vector3.one * cellSize);

            if (Application.isPlaying)
            {
                Collider2D hit = Physics2D.OverlapBox(drawPos, Vector2.one * (cellSize * 0.8f), 0);
                if (hit != null)
                {
                    if (hit.CompareTag("wall"))
                    {
                        Gizmos.color = Color.black;
                        Gizmos.DrawCube(drawPos, Vector3.one * cellSize * 0.9f);
                    }
                    else if (hit.CompareTag("breakable"))
                    {
                        Gizmos.color = new Color(0.5f, 0.5f, 0.5f, 0.4f);
                        Gizmos.DrawCube(drawPos, Vector3.one * cellSize * 0.9f);
                    }
                    else if (hit.CompareTag("target"))
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawCube(drawPos, Vector3.one * cellSize * 0.5f);
                    }
                    else if (hit.CompareTag("bomb"))
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawSphere(drawPos, cellSize * 0.3f);
                    }
                }
            }
        }

        if (Application.isPlaying && env != null)
        {
            Vector3 targetWorldPos = new Vector3(
                env.targetX * cellSize,
                env.targetY * cellSize,
                0);

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(agentPos, targetWorldPos);
        }
    }
}