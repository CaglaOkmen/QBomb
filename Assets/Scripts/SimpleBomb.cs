using UnityEngine;

public class SimpleBomb : MonoBehaviour
{
    private QBombENV_sc env;
    private float cellSize;
    public GameObject patlama;

    public int explosionSteps = 3; // 3 adim sonra patlar
    public int currentStep = 0;

    int bombGridX;
    int bombGridY;

    public enum BombOwner { Agent, Target }
    public BombOwner owner;

    Vector2Int[] dirs = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    private void Start()
    {
        env = FindObjectOfType<QBombENV_sc>();
        if (env != null)
        {
            cellSize = env.cellSize;
        }

        bombGridX = Mathf.RoundToInt(transform.position.x / cellSize);
        bombGridY = Mathf.RoundToInt(transform.position.y / cellSize);
    }

    public void SetOwner(BombOwner bombOwner)
    {
        owner = bombOwner;
    }

    // Ajan her adim attiginda Env tarafindan bu cagrilir
    public void OnStep()
    {
        currentStep++;

        if (currentStep >= explosionSteps)
        {
            Patlat();
        }
    }

    void Patlat()
    {
        DestroyAll(bombGridX, bombGridY);
        if (env != null) env.MarkBombDanger(bombGridX, bombGridY, false);

        Destroy(gameObject);
        // Bomba sahibine gore aktif bomba sayisini azalt
        if (env != null)
        {
            if (owner == BombOwner.Agent)
                env.agentBombActive = false;
            else if (owner == BombOwner.Target)
                env.targetBombActive = false;
        }
    }

    void DestroyAll(int x, int y)
    {
        foreach (var d in dirs)
        {
            int nx = x + d.x;
            int ny = y + d.y;
            if (env != null && nx >= 0 && nx < env.width && ny >= 0 && ny < env.height)
            {
                if (env.map[nx, ny] == 1) // Kirilabilir duvar
                {
                    env.map[nx, ny] = 0; // Harita verisini temizle

                    env.WallDestroyed(); // Istatistik icin

                    Vector3 wallPos = new Vector3(nx * cellSize, ny * cellSize, 0);

                    Collider2D[] hits = Physics2D.OverlapBoxAll(wallPos, Vector2.one * (cellSize * 0.8f), 0);
                    foreach (var hit in hits)
                    {
                        if (hit.gameObject.CompareTag("breakable"))
                        {
                            Destroy(hit.gameObject);
                            break; 
                        }
                    }
                }

                if (env.map[nx, ny] != 2)
                {
                    Vector3 pat = new Vector3(nx * cellSize, ny * cellSize, -1f);
                    if (patlama != null) Instantiate(patlama, pat, Quaternion.identity);
                }

                // Hedef kontrolu
                if (nx == env.targetX && ny == env.targetY)
                {
                    env.kill = true;
                }

                // Ajan kontrolu
                if (nx == env.gridX && ny == env.gridY)
                {
                    if (owner == BombOwner.Agent) env.deathType = QBombENV_sc.DeathType.Suicide; // Kendi bombasi
                    else env.deathType = QBombENV_sc.DeathType.KilledByTarget; // Rakip bombasi

                    env.isAlive = false;
                }
            }
        }
    }
}