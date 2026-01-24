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
    public bool bombActive;
    public bool[,] dangerMap;
    
    public int targetX, targetY;
    public bool kill;
    private bool terminated;

    public int[,] map; // 0:zemin, 1:breakable, 2:unbreakable

    private Pathfinder pathfinder;
    private GameObject activeBombObject;

    private int consecutiveWaitCount = 0; // Peþ peþe bekleme sayýsý

    private void Start()
    {
        if (agentObject != null)
        {
            pathfinder = agentObject.GetComponent<Pathfinder>();
            if (pathfinder == null) pathfinder = agentObject.AddComponent<Pathfinder>();
        }
        else
        {
            Debug.LogError("HATA: agentObject Inspector'dan atanmadý!");
        }

        if (targetObject == null)
        {
            Debug.LogError("HATA: targetObject Inspector'dan atanmadý!");
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
                        if (Random.Range(0, 2) == 0) // %50 ihtimalle kýrýlabilir duvar
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

        if (agentObject != null)
        {
            agentObject.transform.position = new Vector3(gridX * cellSize, gridY * cellSize, 0);
        }

        targetX = width - 2;
        targetY = height - 2;
        kill = false;

        if (targetObject != null)
        {
            targetObject.transform.position = new Vector3(targetX * cellSize, targetY * cellSize, 0);
        }

        consecutiveWaitCount = 0;
    }

    public (float reward, bool done) Step(int action)
    {
        float reward = -0.01f;
        int newX = gridX;
        int newY = gridY;

        if (!isAlive) return (-200f, true);
        if (kill) return (200f, true);
        // --- BEKLEME (5) ---
        if (action == 5) 
        {
            if (!bombActive) reward -= 0.5f;
            consecutiveWaitCount++;
            if (consecutiveWaitCount > 3)
            {
                // 3 kereden fazla beklerse artan ceza
                reward -= 0.1f * (consecutiveWaitCount - 3);
            }
        }
        else
        {
            consecutiveWaitCount = 0;
        }

        float oldDistanceToTarget = float.MaxValue;
        if (pathfinder != null && targetX != -1)
        {
            oldDistanceToTarget = pathfinder.GetDistanceToTarget(gridX, gridY, targetX, targetY);
        }

        // --- HAREKETLER (0-3) ---
        if (action == 0) newY += 1;      // Yukarý
        else if (action == 1) newY -= 1; // Aþaðý
        else if (action == 2) newX += 1; // Sað
        else if (action == 3) newX -= 1; // Sol

        // --- BOMBA KOYMA (4) ---
        else if (action == 4)
        {
            if (!bombActive)
            {
                activeBombObject = Instantiate(bomb, new Vector3(gridX * cellSize, gridY * cellSize, 0), Quaternion.identity);
                bombActive = true;

                bool threatensTarget = IsTargetInBlastRange(gridX, gridY);
                int broken = stratejiControl(gridX, gridY);

                // Stratejik hamle mi
                if (!threatensTarget)
                {
                    if (broken == 0)
                    {
                        // Hedef menzilde degil ve duvar kirilmadi
                        float dist = Mathf.Abs(targetX - gridX) + Mathf.Abs(targetY - gridY);

                        if (dist < 3.0f)
                        {
                            // Hedef yakýnda, belki gelir stratejik
                            reward -= 0.1f;
                        }
                        else if (dist == 3.0f)
                        {
                            reward -= 0.5f; // Hedef gelebilir ama daha dusuk ihtimal
                        }
                        else
                        {
                            // Hedef uzakta, etraf boþ
                            reward -= 2.0f;
                        }
                    }
                    else // duvar kiriyorsa 
                    {
                        reward += (broken * 5f);
                    }

                }
                else // Hedef menzildeyse 
                {
                    reward += 20.0f;
                }
            }
            else // Bomba varken
            {
                reward -= 5f;
            }
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

        
        if (bombActive && activeBombObject != null)
        {
            SimpleBomb bombScript = activeBombObject.GetComponent<SimpleBomb>();
            if (bombScript != null) bombScript.OnStep();
        }

        // Target hareketi ajanla ayný zamanli
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
        bombActive = false;

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
