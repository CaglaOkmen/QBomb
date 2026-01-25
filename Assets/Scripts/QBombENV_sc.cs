using System.Collections.Generic;
using UnityEngine;

public class QBombENV_sc : MonoBehaviour
{
    public GameObject wall;
    public GameObject breakable_wall;
    public GameObject bomb;
    public GameObject agentObject;
    public GameObject targetObject;

    [Header("Env Bilgisi")]
    public float cellSize = 2.0f;
    public int width = 9, height = 7;
    public int numActions = 6;

    [Header("State Bilgisi")]
    public int gridX;
    public int gridY;
    public bool isAlive;
    public bool agentBombActive;
    public bool targetBombActive;

    public bool[,] dangerMap;
    
    public int targetX, targetY;
    public bool kill;
    private bool terminated;

    public int[,] map; // 0:zemin, 1:breakable, 2:unbreakable

    private Pathfinder pathfinder;

    private List<GameObject> activeBombs = new List<GameObject>(); // Aktif tum bombalar
    public enum DeathType { None, Suicide, KilledByTarget }
    public DeathType deathType = DeathType.None;

    private int consecutiveWaitCount = 0;

    public bool bombActive
    {
        get { return agentBombActive || targetBombActive; }
        set { agentBombActive = value; }
    }

    private string deathReason = "";

    public void LogDeath(string reason)
    {
        deathReason = reason;
    }

    private void Start()
    {
        if (agentObject != null)
        {
            pathfinder = agentObject.GetComponent<Pathfinder>();
            if (pathfinder == null) pathfinder = agentObject.AddComponent<Pathfinder>();
        }
        CreateGrid();
        ResetAgentAndTarget();
    }

    public void CreateGrid()
    {
        map = new int[width, height];
        dangerMap = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 pos = new Vector3(x * cellSize, y * cellSize, 0);
                dangerMap[x, y] = false;

                if (x == 0 || y == 0 || x == width - 1 || y == height - 1 || (x % 2 == 0 && y % 2 == 0))
                {
                    map[x, y] = 2;
                    Instantiate(wall, pos, Quaternion.identity);
                }
                else
                {
                    // Baþlangýç (1,1) ve etrafýný, Hedef (son-2, son-2) ve etrafýný boþ býrak
                    if ((x != 1 || y != 1) && (x != 1 || y != 2) && (x != 2 || y != 1) && (x != width - 2 || y != height - 2)
                        && (x != width - 2 || y != height - 3) && (x != width - 3 || y != height - 2))
                    {
                        if (Random.Range(0, 2) == 0) // %50 ihtimalle kirilabilir duvar
                        {
                            map[x, y] = 1;
                            Instantiate(breakable_wall, pos, Quaternion.identity);
                        }
                        else map[x, y] = 0;
                    }
                    else map[x, y] = 0;
                }
            }
        }
    }

    // Ajan ve Targeti sifirla
    void ResetAgentAndTarget()
    {
        gridX = 1;
        gridY = 1;
        isAlive = true;
        deathType = DeathType.None; // Sifirla

        if (agentObject != null)
            agentObject.transform.position = new Vector3(gridX * cellSize, gridY * cellSize, 0);

        kill = false;
        consecutiveWaitCount = 0;

        bool randomPos = false;
        if (targetObject != null)
        {
            CurriculumTarget ct = targetObject.GetComponent<CurriculumTarget>();
            if (ct != null && ct.currentPhase == 1) randomPos = true;
        }

        if (randomPos) SetRandomTargetPosition(); // Faz 1: Rastgele
        else { targetX = width - 2; targetY = height - 2; } // Faz 2 ve 3: Sabit

        if (targetObject != null)
            targetObject.transform.position = new Vector3(targetX * cellSize, targetY * cellSize, 0);
    }

    void SetRandomTargetPosition()
    {
        int attempts = 30;
        while (attempts > 0)
        {
            int rx = Random.Range(1, width - 1);
            int ry = Random.Range(1, height - 1);
            if (Mathf.Abs(rx - gridX) + Mathf.Abs(ry - gridY) < 3) { attempts--; continue; }
            if (map[rx, ry] == 0) { targetX = rx; targetY = ry; return; }
            attempts--;
        }
        targetX = width - 2;
        targetY = height - 2;
    }

    // Bomba koyma fonksiyonu - owner bilgisiyle
    public GameObject PlaceBomb(int x, int y, SimpleBomb.BombOwner owner)
    {
        GameObject bombObj = Instantiate(bomb, new Vector3(x * cellSize, y * cellSize, 0), Quaternion.identity);
        SimpleBomb bombScript = bombObj.GetComponent<SimpleBomb>();

        if (bombScript != null) bombScript.SetOwner(owner);
        activeBombs.Add(bombObj);

        if (owner == SimpleBomb.BombOwner.Agent) agentBombActive = true;
        else if (owner == SimpleBomb.BombOwner.Target) targetBombActive = true;

        return bombObj;
    }

    public (float reward, bool done) Step(int action)
    {
        float reward = -0.01f;
        int newX = gridX;
        int newY = gridY;

        if (!isAlive)
        {
            if (deathType == DeathType.Suicide)
            {
                Debug.Log("--- SUICIDE (-300) ---");
                return (-300f, true);
            }
            else
            {
                Debug.Log("--- KILLED BY TARGET (-100) ---");
                return (-100f, true);
            }
        }
        if (kill)
        {
            Debug.Log("--- TARGET ELIMINATED (+200) ---");
            return (200f, true);
        }
        // --- BEKLEME (5) ---
        if (action == 5) 
        {
            if (!agentBombActive) reward -= 0.5f;
            consecutiveWaitCount++;
            if (consecutiveWaitCount > 3) reward -= 0.1f * (consecutiveWaitCount - 3);
        }
        else consecutiveWaitCount = 0;

        float oldDistanceToTarget = float.MaxValue;
        if (pathfinder != null && targetX != -1)
            oldDistanceToTarget = pathfinder.GetDistanceToTarget(gridX, gridY, targetX, targetY);

        // --- HAREKETLER (0-3) ---
        if (action == 0) newY += 1; // Yukari
        else if (action == 1) newY -= 1; // Asagi
        else if (action == 2) newX += 1; // Sag
        else if (action == 3) newX -= 1; // Sol

        // --- BOMBA KOYMA (4) ---
        else if (action == 4)
        {
            if (!agentBombActive)
            {
                PlaceBomb(gridX, gridY, SimpleBomb.BombOwner.Agent);
                bool threatensTarget = IsTargetInBlastRange(gridX, gridY);
                int broken = stratejiControl(gridX, gridY);

                if (!threatensTarget)
                {
                    if (broken == 0)
                    {
                        // Hedef menzilde degil ve duvar kirilmadi
                        float dist = Mathf.Abs(targetX - gridX) + Mathf.Abs(targetY - gridY);
                        if (dist < 3.0f) reward -= 0.1f; // Hedef yakinda, belki gelir stratejik
                        else if (dist == 3.0f) reward -= 0.5f; // Hedef gelebilir ama daha dusuk ihtimal
                        else reward -= 2.0f; // Hedef uzakta, etraf bos
                    }
                    else reward += (broken * 5f); // duvar kiriyorsa 
                }
                else reward += 20.0f; // Hedef menzildeyse 
            }
            else reward -= 5f; // Bomba varken
        }

        // Targete yakinlasma uzaklasma
        if (pathfinder != null && targetX != -1 && action != 4 && action != 5)
        {
            float newDistanceToTarget = pathfinder.GetDistanceToTarget(gridX, gridY, targetX, targetY);

            if (newDistanceToTarget < oldDistanceToTarget && newDistanceToTarget != float.MaxValue)
                reward += 0.1f;
            else
                reward -= 0.05f;
        }

        // Duvar kontrolu
        if (action >= 0 && action <= 3)
        {
            bool collision = false;
            if (newX < 0 || newX >= width || newY < 0 || newY >= height) collision = true;
            else if (map[newX, newY] != 0) collision = true;

            if (collision)
            {
                reward -= 0.5f;
                newX = gridX; newY = gridY;
            }
            else
            {
                gridX = newX;
                gridY = newY;
            }
        }
        if (pathfinder.IsInDanger(gridX, gridY)) reward -= 0.1f;

        if (agentObject != null)
            agentObject.transform.position = new Vector3(gridX * cellSize, gridY * cellSize, 0);

        // Tum bombalari guncelle
        UpdateAllBombs();

        // Target hareketi
        if (targetObject != null)
        {
            CurriculumTarget ct = targetObject.GetComponent<CurriculumTarget>();
            if (ct != null)
            {
                ct.OnTargetStep();
            }
        }
        return (reward, terminated);
    }

    void UpdateAllBombs()
    {
        // Null bombalari temizle
        activeBombs.RemoveAll(b => b == null);

        // Her bombayý güncelle
        foreach (GameObject bombObj in activeBombs)
        {
            if (bombObj != null)
            {
                SimpleBomb bombScript = bombObj.GetComponent<SimpleBomb>();
                if (bombScript != null)
                {
                    bombScript.OnStep();
                }
            }
        }
    }

    bool IsTargetInBlastRange(int bombX, int bombY)
    {
        if (targetX == -1 || targetY == -1) return false;

        Vector2Int[] dirs = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var d in dirs)
        {
            int nx = bombX + d.x;
            int ny = bombY + d.y;

            if (nx == targetX && ny == targetY) return true;
        }
        return false;
    }

    int stratejiControl(int x, int y)
    {
        int brokenCount = 0;
        Vector2Int[] dirs = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        foreach (var d in dirs)
        {
            int nx = x + d.x;
            int ny = y + d.y;
            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
            {
                dangerMap[nx, ny] = true;
                if (map[nx, ny] == 1) brokenCount++;
            }
        }
        return brokenCount;
    }

    public void Reset()
    {
        // Duvarlar ve Bombalarý temizle
        foreach (var obj in GameObject.FindGameObjectsWithTag("breakable")) Destroy(obj);
        foreach (var obj in GameObject.FindGameObjectsWithTag("wall")) Destroy(obj);
        foreach (var obj in GameObject.FindGameObjectsWithTag("bomb")) Destroy(obj);

        CreateGrid(); // Haritayi yeniden olustur

        terminated = false;
        agentBombActive = false;
        targetBombActive = false;

        // Ajan ve Target pozisyonlarýný sifirla
        ResetAgentAndTarget();

        if (targetObject != null)
        {
            CurriculumTarget ct = targetObject.GetComponent<CurriculumTarget>();
            if (ct != null)
            {
                ct.OnEpisodeEnd();
            }
        }
    }
}