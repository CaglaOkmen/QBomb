using UnityEngine;

public class SimpleBomb : MonoBehaviour
{
    private QBombENV_sc env;
    private float cellSize;
    public GameObject patlama;

    public int explosionSteps = 3; // 3 adim sonra patlar
    private int currentStep = 0;

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
                env.dangerMap[nx, ny] = false;

                if (env.map[nx, ny] == 1) // Kirilabilir duvar
                {
                    env.map[nx, ny] = 0;
                    Vector3 wallPos = new Vector3(nx * cellSize, ny * cellSize, 0);
                    Collider2D hit = Physics2D.OverlapBox(wallPos, Vector2.one * 0.5f, 0);
                    if (hit != null && hit.gameObject.CompareTag("breakable")) Destroy(hit.gameObject);
                }

                if (env.map[nx, ny] != 2)
                {
                    Vector3 pat = new Vector3(nx * cellSize, ny * cellSize, -1f);
                    if (patlama != null) Instantiate(patlama, pat, Quaternion.identity);
                }

                // Hedef kontrolu
                if (nx == env.targetX && ny == env.targetY)
                {
                    if (owner == BombOwner.Target)
                        env.LogDeath("Target kendi bombasýyla öldü.");
                    else
                        env.LogDeath("Target, Ajan'ýn bombasýyla öldü.");

                    env.kill = true;
                }

                // Ajan kontrolu
                if (nx == env.gridX && ny == env.gridY)
                {
                    if (owner == BombOwner.Agent)
                        env.LogDeath("Ajan kendi bombasýyla öldü.");
                    else
                        env.LogDeath("Ajan, Target'ýn bombasýyla öldü.");

                    env.isAlive = false;
                }
            }
        }
    }
}
