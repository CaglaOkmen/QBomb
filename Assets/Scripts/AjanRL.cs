using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;

public class AjanRL : MonoBehaviour
{
    [Header("Hiperparametreler")]
    public int maxEpisode = 3000;
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
    private string saveFileName = "bomberman_mlp_v2.json";

    [Header("Guvenlik katmaný")]
    public bool useSafetyRule = true;

    [Header("Gorselleme")]
    public float currentEpisodeReward = 0;
    public int currentEpisodeSteps = 0;
    public int currentEpisodeIndex = 0;

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

    void Awake()
    {
        // 5 kanal (IsWall, IsBreakable, IsTarget, IsBomb, BombTimer)
        int gridCells = observationPattern.Length;
        int channelsPerCell = 5;
        int gridInputs = gridCells * channelsPerCell;

        // Global inputs: Target X,Y, Targete uzaklýk, tehlikede mi, Ajan ve Target bombalai aktif mi
        int globalInputs = 6;

        inputSize = gridInputs + globalInputs;

        env = FindObjectOfType<QBombENV_sc>();
        pathfinder = gameObject.AddComponent<Pathfinder>();
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
        if (Input.GetKeyDown(KeyCode.T)) { StopAllCoroutines(); StartCoroutine(AjanTesting()); }
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

            // Onehot kodlama ve Bomba zamani
            observations[index++] = isWall ? 1.0f : 0.0f;
            observations[index++] = isBreakable ? 1.0f : 0.0f;
            observations[index++] = isTarget ? 1.0f : 0.0f;
            observations[index++] = isBomb ? 1.0f : 0.0f;
            observations[index++] = bombTimer; 

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

        // Ajan Bomba durumu
        observations[index++] = env.agentBombActive ? 1.0f : 0.0f;

        // Target Bomba durumu
        observations[index++] = env.targetBombActive ? 1.0f : 0.0f;

        // Tehlike durumu
        bool inDanger = pathfinder.IsInDanger(env.gridX, env.gridY);
        observations[index++] = inDanger ? 1.0f : 0.0f;

        return observations;
    }

    IEnumerator AjanTraining()
    {
        int maxStepsPerEpisode = 600;

        winCount = 0; deathCount = 0; timeoutCount = 0;

        print("=== TRAINING START ===");
        isTraining = true;

        for (int ep = 1; ep <= maxEpisode; ep++)
        {
            currentEpisodeIndex = ep;
            env.Reset();

            float[] state = GetObservation();
            bool done = false;
            float totalReward = 0;
            int steps = 0;

            currentEpisodeReward = 0;
            currentEpisodeSteps = 0;

            System.Array.Clear(actionCounts, 0, actionCounts.Length);

            while (!done && steps < maxStepsPerEpisode)
            {
                int action = 0;
                float[] qValues = mlp.Forward(state);

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
                    }
                    else
                    {
                        action = GetActionFromQ(qValues);
                    }
                }

                if (action < actionCounts.Length) actionCounts[action]++;

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

            // Ýstatistikler
            string resultReason = "TIMEOUT";
            if (env.kill) { resultReason = "WIN"; winCount++; }
            else if (!env.isAlive) { resultReason = "DIED"; deathCount++; }
            else { timeoutCount++; }

            if (epsilon > minEpsilon)
            {
                epsilon = Mathf.Max(minEpsilon, epsilon * epsilonDecay);
            }

            string actionReport = $"Actions => Y:{actionCounts[0]} A:{actionCounts[1]} S:{actionCounts[2]} Sol:{actionCounts[3]} B:{actionCounts[4]} WAIT:{actionCounts[5]}";

            print($"Ep: {ep} | {resultReason} | R: {totalReward:F1} | Eps: {epsilon:F3} | {actionReport}");

            if (ep % 20 == 0)
            {
                print($"20 Ep Durum: Wins:{winCount} Deaths:{deathCount} Timeouts:{timeoutCount}");
                winCount = 0; deathCount = 0; timeoutCount = 0;
                SaveNetwork();
            }
        }
        SaveNetwork();
        isTraining = false;
    }

    IEnumerator AjanTesting()
    {
        print("=== TESTING START ===");
        isTraining = false;
        env.Reset();

        bool done = false;
        int steps = 0;

        int testMaxSteps = 300;

        while (!done && steps < testMaxSteps)
        {
            float[] state = GetObservation();
            float[] qValues = mlp.Forward(state);
            int rlAction = GetActionFromQ(qValues);

            string qLog = $"Step {steps} Q-Values: ";
            qLog += $"Up:{qValues[0]:F2} Down:{qValues[1]:F2} Right:{qValues[2]:F2} Left:{qValues[3]:F2} ";
            qLog += $"BOMB:{qValues[4]:F2} Wait:{qValues[5]:F2}";

            print(qLog);
            int finalAction = rlAction;

            if (useSafetyRule && pathfinder.IsInDanger(env.gridX, env.gridY))
            {
                int safeMove = pathfinder.GetSafeMove(env.gridX, env.gridY);
                if (safeMove != -1)
                {
                    finalAction = safeMove;
                    print($"TEHLÝKE TESPÝT EDÝLDÝ! Kaçýþ Modu: {finalAction} (RL kararý {rlAction} ezildi)");
                }
                else
                {
                    print($"TEHLÝKE! Ancak güvenli rota bulunamadý, RL kararý uygulanýyor: {finalAction}");
                }
            }
            else
            {
                if (!useSafetyRule && pathfinder.IsInDanger(env.gridX, env.gridY))
                {
                    print($"TEHLÝKE VAR AMA 'SAFETY' KAPALI! RL Kararý: {finalAction}");
                }
                else
                {
                    print($"Güvenli. RL Kararý Uygulanýyor: {finalAction}");
                }
            }

            (float r, bool t) = env.Step(finalAction);
            done = t;
            steps++;

            yield return new WaitForSeconds(0.5f);
        }

        string resultLog = "";
        if (env.kill)
        {
            resultLog = "VICTORY! (Target Eliminated)";
            Debug.Log($"<color=green>{resultLog}</color>");
        }
        else if (!env.isAlive)
        {
            resultLog = "DEFEAT! (Agent Died)";
            Debug.Log($"<color=red>{resultLog}</color>");
        }
        else
        {
            resultLog = "TIMEOUT! (Steps Exceeded)";
            Debug.Log($"<color=yellow>{resultLog}</color>");
        }

        print($"TEST FINISHED: {resultLog} in {steps} steps.");
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
        string path = System.IO.Path.Combine(Application.persistentDataPath, saveFileName);
        mlp.SaveModel(path);
    }

    void LoadNetwork()
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, saveFileName);
        if (File.Exists(path))
        {
            mlp.LoadModel(path);
            print("Model loaded: " + path);
        }
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