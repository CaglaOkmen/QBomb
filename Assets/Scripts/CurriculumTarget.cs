using UnityEngine;
using System.Collections.Generic;

public class CurriculumTarget : MonoBehaviour
{
    private QBombENV_sc env;

    [Header("Faz Ayarlari")]
    public int currentPhase = 1;
    public int phase1Duration = 300;
    public int phase2Duration = 600;

    [Header("Faz 3: Strateji ve Denge")]
    [Range(0f, 1f)] public float aggressionLevel = 0.5f;     // 1: Saldirgan, 0: Kacan
    [Range(0f, 1f)] public float randomnessFactor = 0.2f;   // Ezberlemeyi engelleme

    [Header("Faz 3: Bomba Parametreleri")]
    public float bombNearAgent = 0.4f;
    public float bombNearWall = 0.6f;

    [Header("Istatistikler")]
    public int totalEpisodes = 0;
    public int currentPhaseEpisodes = 0;

    private int escapeStepsRemaining = 0;
    private Vector2Int safeEscapeTarget;

    void Start()
    {
        env = FindObjectOfType<QBombENV_sc>();
    }

    public void OnTargetStep()
    {
        if (env == null || !env.isAlive || env.kill) return;

        if (currentPhase == 1) return;
        if (currentPhase == 2) { MoveRandomly(); return; }

        if (escapeStepsRemaining > 0)
        {
            HandleEscapeSequence();
            return;
        }

        if (Random.value < randomnessFactor)
        {
            MoveRandomly();
            return;
        }

        ExecutePhase3Behavior();
    }

    void ExecutePhase3Behavior()
    {
        // Tehlikeliyse kac
        if (env.dangerMap[env.targetX, env.targetY])
        {
            MoveToSafestNeighbor();
            return;
        }

        // Bomba koy
        if (!env.targetBombActive)
        {
            bool nearAgent = ManhattanDistanceToPlayer() <= 2;
            bool nearWall = IsNearBreakableWall();

            bool shouldPlace = (nearAgent && Random.value < bombNearAgent) ||
                               (nearWall && Random.value < bombNearWall);

            if (shouldPlace)
            {
                Vector2Int escape = FindSafeEscapeCell();
                if (escape.x != -1)
                {
                    PlaceBombAndEscape(escape);
                    return;
                }
            }
        }

        // Agresiflik seviyesine gore yaklas veya kac
        if (Random.value < aggressionLevel)
            MoveTowardPlayer();
        else
            MoveAwayFromPlayer();
    }

    void MoveRandomly()
    {
        var safeMoves = GetValidSafeMoves();
        if (safeMoves.Count > 0)
            ExecuteMove(safeMoves[Random.Range(0, safeMoves.Count)]);
        else
            MoveToSafestNeighbor();
    }

    void HandleEscapeSequence()
    {
        int dir = GetDirectionTo(safeEscapeTarget);
        if (dir != -1)
            ExecuteMove(dir);
        else
            MoveToSafestNeighbor();

        escapeStepsRemaining--;
    }

    void MoveTowardPlayer()
    {
        MoveToBestNeighbor(pos => -ManhattanDistance(pos, env.gridX, env.gridY));
    }

    void MoveAwayFromPlayer()
    {
        MoveToBestNeighbor(pos => ManhattanDistance(pos, env.gridX, env.gridY));
    }

    void MoveToSafestNeighbor()
    {
        MoveToBestNeighbor(pos =>
        {
            if (!env.dangerMap[pos.x, pos.y]) return 1000; // Güvenliyse iyi
            return -1000; // Tehlikeliyse kotu
        });
    }

    void MoveToBestNeighbor(System.Func<Vector2Int, float> scoreFunc)
    {
        float bestScore = float.MinValue;
        int bestDir = -1;

        for (int dir = 0; dir < 4; dir++)
        {
            Vector2Int next = GetNextPos(dir);
            if (!IsValidPos(next.x, next.y)) continue;

            float score = scoreFunc(next);
            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dir;
            }
        }

        if (bestDir != -1)
            ExecuteMove(bestDir);
        else
            MoveRandomly();
    }

    List<int> GetValidSafeMoves()
    {
        List<int> moves = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            Vector2Int n = GetNextPos(i);
            if (IsValidPos(n.x, n.y) && !env.dangerMap[n.x, n.y])
                moves.Add(i);
        }
        return moves;
    }

    void ExecuteMove(int dir)
    {
        Vector2Int n = GetNextPos(dir);
        if (IsValidPos(n.x, n.y))
        {
            env.targetX = n.x;
            env.targetY = n.y;
            transform.position = new Vector3(n.x * env.cellSize, n.y * env.cellSize, 0);
        }
    }

    bool IsValidPos(int x, int y)
    {
        return x >= 0 && x < env.width && y >= 0 && y < env.height && env.map[x, y] == 0;
    }

    Vector2Int GetNextPos(int dir)
    {
        int nx = env.targetX, ny = env.targetY;
        switch (dir)
        {
            case 0: ny++; break; // Yukari
            case 1: ny--; break; // Asagi
            case 2: nx++; break; // Sag
            case 3: nx--; break; // Sol
        }
        return new Vector2Int(nx, ny);
    }

    int ManhattanDistanceToPlayer() =>
        Mathf.Abs(env.targetX - env.gridX) + Mathf.Abs(env.targetY - env.gridY);

    int ManhattanDistance(Vector2Int p, int x, int y) =>
        Mathf.Abs(p.x - x) + Mathf.Abs(p.y - y);

    bool IsNearBreakableWall()
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2Int n = GetNextPos(i);
            if (n.x >= 0 && n.x < env.width && n.y >= 0 && n.y < env.height && env.map[n.x, n.y] == 1)
                return true;
        }
        return false;
    }

    void PlaceBombAndEscape(Vector2Int escapeTarget)
    {
        env.PlaceBomb(env.targetX, env.targetY, SimpleBomb.BombOwner.Target);
        MarkBombDanger(true);
        safeEscapeTarget = escapeTarget;
        escapeStepsRemaining = 4;
    }

    int GetDirectionTo(Vector2Int target)
    {
        int dx = target.x - env.targetX;
        int dy = target.y - env.targetY;

        if (dx > 0 && IsValidMove(2)) return 2;
        if (dx < 0 && IsValidMove(3)) return 3;
        if (dy > 0 && IsValidMove(0)) return 0;
        if (dy < 0 && IsValidMove(1)) return 1;

        return -1;
    }

    bool IsValidMove(int dir)
    {
        Vector2Int n = GetNextPos(dir);
        return IsValidPos(n.x, n.y);
    }

    Vector2Int FindSafeEscapeCell()
    {
        bool[,] simDanger = new bool[env.width, env.height];
        for (int x = 0; x < env.width; x++)
            for (int y = 0; y < env.height; y++)
                simDanger[x, y] = env.dangerMap[x, y];

        int tx = env.targetX, ty = env.targetY;
        simDanger[tx, ty] = true;
        if (tx + 1 < env.width) simDanger[tx + 1, ty] = true;
        if (tx - 1 >= 0) simDanger[tx - 1, ty] = true;
        if (ty + 1 < env.height) simDanger[tx, ty + 1] = true;
        if (ty - 1 >= 0) simDanger[tx, ty - 1] = true;

        // BFS ile en yakin guvenli hucre bulma
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        queue.Enqueue(new Vector2Int(tx, ty));
        visited.Add(new Vector2Int(tx, ty));

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.right, Vector2Int.left };

        while (queue.Count > 0)
        {
            Vector2Int curr = queue.Dequeue();
            if (!simDanger[curr.x, curr.y]) return curr;

            foreach (var d in dirs)
            {
                Vector2Int next = curr + d;
                if (IsValidPos(next.x, next.y) && !visited.Contains(next))
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return new Vector2Int(-1, -1);
    }

    void MarkBombDanger(bool active)
    {
        int tx = env.targetX, ty = env.targetY;
        env.dangerMap[tx, ty] = active;
        if (tx + 1 < env.width) env.dangerMap[tx + 1, ty] = active;
        if (tx - 1 >= 0) env.dangerMap[tx - 1, ty] = active;
        if (ty + 1 < env.height) env.dangerMap[tx, ty + 1] = active;
        if (ty - 1 >= 0) env.dangerMap[tx, ty - 1] = active;
    }

    public void OnEpisodeEnd()
    {
        escapeStepsRemaining = 0;
        totalEpisodes++;
        currentPhaseEpisodes++;
        CheckPhaseTransition();
    }

    void CheckPhaseTransition()
    {
        if (currentPhase == 1 && currentPhaseEpisodes >= phase1Duration)
        {
            currentPhase = 2;
            currentPhaseEpisodes = 0;
        }
        else if (currentPhase == 2 && currentPhaseEpisodes >= phase2Duration)
        {
            currentPhase = 3;
            currentPhaseEpisodes = 0;
        }
    }
}