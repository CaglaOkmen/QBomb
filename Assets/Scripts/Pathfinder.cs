using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Pathfinder : MonoBehaviour
{
    private QBombENV_sc env;

    public bool showGizmos = true;
    private List<Vector2Int> lastCalculatedPath;
    private Vector2Int? currentTargetCell;
    private List<Vector2Int> escapePath;

    private enum PathType { None, Clear, Breakable }
    private PathType currentPathType = PathType.None;

    [Header("Risk Parametreleri")]
    public float dangerWeight = 50f;
    public float enemyProximityWeight = 15f;
    public float deadEndWeight = 20f;
    public int safetyRadius = 2;

    private float[,] riskMap;

    private class Node
    {
        public int x, y;
        public int gCost;
        public int hCost;
        public float riskCost;
        public Node parent;

        public float fCost { get { return gCost + hCost + riskCost; } }

        public Node(int x, int y)
        {
            this.x = x;
            this.y = y;
            this.riskCost = 0;
        }
    }

    void Start()
    {
        env = FindObjectOfType<QBombENV_sc>();
        if (env != null)
        {
            riskMap = new float[env.width, env.height];
        }
    }

    void Update()
    {
        if (env != null)
        {
            UpdateRiskMap();
        }
    }

    public float GetDistanceToTarget(int currentX, int currentY, int targetX, int targetY)
    {
        // Duvarsýz yol varsa
        currentPathType = PathType.Clear;
        List<Vector2Int> path = FindPath(new Vector2Int(currentX, currentY), new Vector2Int(targetX, targetY), false);

        // Duvarsýz yol yoksa, kirilabilir duvarlý yol dene
        if (path == null)
        {
            currentPathType = PathType.Breakable;
            path = FindPath(new Vector2Int(currentX, currentY), new Vector2Int(targetX, targetY), true);
        }

        lastCalculatedPath = path;

        if (path != null) return path.Count;

        currentPathType = PathType.None;
        return float.MaxValue;
    }

    public int GetSafeMove(int currentX, int currentY)
    {
        UpdateRiskMap();

        Vector2Int currentPos = new Vector2Int(currentX, currentY);
        Vector2Int targetPos = FindNearestSafeCell(currentPos);

        currentTargetCell = targetPos;

        if (targetPos == currentPos)
        {
            return -1;
        }

        List<Vector2Int> path = FindPath(currentPos, targetPos, false);
        escapePath = path;
        // lastCalculatedPath = path; 

        if (path != null && path.Count > 0)
        {
            Vector2Int nextStep = path[0];

            if (nextStep.x > currentX) return 2;
            if (nextStep.x < currentX) return 3;
            if (nextStep.y > currentY) return 0;
            if (nextStep.y < currentY) return 1;
        }

        return GetRandomMove(currentX, currentY);
    }

    private void UpdateRiskMap()
    {
        if (env == null || riskMap == null) return;

        for (int x = 0; x < env.width; x++)
            for (int y = 0; y < env.height; y++)
                riskMap[x, y] = 0;

        // Bomba tehlikesi
        for (int x = 0; x < env.width; x++)
            for (int y = 0; y < env.height; y++)
                if (env.dangerMap[x, y]) riskMap[x, y] += dangerWeight;

        // Rakibe yakinlik
        if (env.targetX >= 0 && env.targetY >= 0)
        {
            for (int x = 0; x < env.width; x++)
            {
                for (int y = 0; y < env.height; y++)
                {
                    int dist = Mathf.Abs(x - env.targetX) + Mathf.Abs(y - env.targetY);
                    if (dist <= safetyRadius)
                    {
                        float proximityRisk = enemyProximityWeight * (1f - dist / (float)safetyRadius);
                        riskMap[x, y] += proximityRisk;
                    }
                }
            }
        }

        // Kör koridor
        for (int x = 0; x < env.width; x++)
        {
            for (int y = 0; y < env.height; y++)
            {
                if (IsWalkable(x, y, false)) 
                {
                    int escapeRoutes = CountEscapeRoutes(x, y);
                    if (escapeRoutes <= 1) riskMap[x, y] += deadEndWeight * (2 - escapeRoutes);
                }
            }
        }
    }

    public int CountEscapeRoutes(int x, int y)
    {
        int count = 0;
        Vector2Int[] directions = { new Vector2Int(0, 1), new Vector2Int(0, -1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };

        foreach (var dir in directions)
        {
            int nx = x + dir.x;
            int ny = y + dir.y;
            if (IsInBounds(nx, ny) && IsWalkable(nx, ny, false)) count++;
        }
        return count;
    }

    private List<Vector2Int> FindPath(Vector2Int startPos, Vector2Int targetPos, bool allowBreakables)
    {
        List<Node> openList = new List<Node>();
        HashSet<string> closedList = new HashSet<string>();

        Node startNode = new Node(startPos.x, startPos.y);
        Node targetNode = new Node(targetPos.x, targetPos.y);

        openList.Add(startNode);

        while (openList.Count > 0)
        {
            Node currentNode = openList.OrderBy(n => n.fCost).ThenBy(n => n.hCost).First();

            if (currentNode.x == targetNode.x && currentNode.y == targetNode.y)
            {
                return RetracePath(startNode, currentNode);
            }

            openList.Remove(currentNode);
            closedList.Add($"{currentNode.x},{currentNode.y}");

            foreach (Vector2Int neighborPos in GetNeighbors(currentNode.x, currentNode.y))
            {
                if (closedList.Contains($"{neighborPos.x},{neighborPos.y}") ||
                    !IsWalkable(neighborPos.x, neighborPos.y, allowBreakables))
                    continue;

                int newGCost = currentNode.gCost + 1;

                if (allowBreakables && env.map[neighborPos.x, neighborPos.y] == 1) newGCost += 2;

                float newRiskCost = riskMap[neighborPos.x, neighborPos.y];

                Node neighborNode = openList.FirstOrDefault(n => n.x == neighborPos.x && n.y == neighborPos.y);
                bool isNewNode = neighborNode == null;

                if (isNewNode || newGCost < neighborNode.gCost)
                {
                    if (isNewNode)
                    {
                        neighborNode = new Node(neighborPos.x, neighborPos.y);
                        openList.Add(neighborNode);
                    }
                    neighborNode.gCost = newGCost;
                    neighborNode.hCost = Mathf.Abs(neighborNode.x - targetNode.x) + Mathf.Abs(neighborNode.y - targetNode.y);
                    neighborNode.riskCost = newRiskCost;
                    neighborNode.parent = currentNode;
                }
            }
        }
        return null;
    }

    private List<Vector2Int> RetracePath(Node startNode, Node endNode)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        Node currentNode = endNode;
        while (currentNode != startNode)
        {
            path.Add(new Vector2Int(currentNode.x, currentNode.y));
            currentNode = currentNode.parent;
        }
        path.Reverse();
        return path;
    }

    private List<Vector2Int> GetNeighbors(int x, int y)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();
        if (IsInBounds(x + 1, y)) neighbors.Add(new Vector2Int(x + 1, y));
        if (IsInBounds(x - 1, y)) neighbors.Add(new Vector2Int(x - 1, y));
        if (IsInBounds(x, y + 1)) neighbors.Add(new Vector2Int(x, y + 1));
        if (IsInBounds(x, y - 1)) neighbors.Add(new Vector2Int(x, y - 1));
        return neighbors;
    }

    private Vector2Int FindNearestSafeCell(Vector2Int currentPos)
    {
        if (IsSafe(currentPos.x, currentPos.y)) return currentPos;

        float minRisk = float.MaxValue;
        Vector2Int bestCell = currentPos;

        for (int distance = 1; distance < Mathf.Max(env.width, env.height); distance++)
        {
            for (int dx = -distance; dx <= distance; dx++)
            {
                for (int dy = -distance; dy <= distance; dy++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) != distance) continue;
                    int x = currentPos.x + dx;
                    int y = currentPos.y + dy;

                    if (IsInBounds(x, y) && IsWalkable(x, y, false))
                    {
                        float risk = riskMap[x, y];
                        if (!env.dangerMap[x, y] && risk < minRisk)
                        {
                            minRisk = risk;
                            bestCell = new Vector2Int(x, y);
                        }
                    }
                }
            }
            if (bestCell != currentPos) return bestCell;
        }
        return currentPos;
    }

    public bool IsInDanger(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return env.dangerMap[x, y];
    }

    private bool IsSafe(int x, int y) => !IsInDanger(x, y);

    private bool IsWalkable(int x, int y, bool allowBreakables)
    {
        if (!IsInBounds(x, y)) return false;
        if (env.map[x, y] == 0) return true;
        if (allowBreakables && env.map[x, y] == 1) return true;
        return false;
    }

    private bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < env.width && y >= 0 && y < env.height;
    }

    private int GetRandomMove(int currentX, int currentY)
    {
        List<int> possibleMoves = new List<int>();
        if (IsWalkable(currentX, currentY + 1, false)) possibleMoves.Add(0);
        if (IsWalkable(currentX, currentY - 1, false)) possibleMoves.Add(1);
        if (IsWalkable(currentX + 1, currentY, false)) possibleMoves.Add(2);
        if (IsWalkable(currentX - 1, currentY, false)) possibleMoves.Add(3);

        if (possibleMoves.Count > 0)
            return possibleMoves[Random.Range(0, possibleMoves.Count)];
        return -1;
    }


    private void OnDrawGizmos()
    {
        if (!showGizmos || env == null) return;

        // Risk haritasi
        if (riskMap != null)
        {
            for (int x = 0; x < env.width; x++)
            {
                for (int y = 0; y < env.height; y++)
                {
                    if (riskMap[x, y] > 0)
                    {
                        float intensity = Mathf.Clamp01(riskMap[x, y] / 100f);
                        Gizmos.color = new Color(1f, 0f, 0f, intensity * 0.5f);
                        Vector3 pos = new Vector3(
                            x * env.cellSize,
                            y * env.cellSize,
                            0);
                        Gizmos.DrawCube(pos, Vector3.one * env.cellSize * 0.8f);
                    }
                }
            }
        }

        // Guvenli hucre
        if (currentTargetCell.HasValue)
        {
            Gizmos.color = Color.yellow;
            Vector3 targetWorldPos = new Vector3(
                currentTargetCell.Value.x * env.cellSize,
                currentTargetCell.Value.y * env.cellSize,
                0);
            Gizmos.DrawWireSphere(targetWorldPos, env.cellSize * 0.4f);
        }

        // Targete yol
        if (lastCalculatedPath != null && lastCalculatedPath.Count > 0)
        {
            // Yol türüne göre renk
            if (currentPathType == PathType.Clear)
                Gizmos.color = Color.green;
            else if (currentPathType == PathType.Breakable)
                Gizmos.color = Color.blue;
            else
                Gizmos.color = Color.yellow;

            Vector3 startPos = transform.position;
            Vector3 firstNodePos = new Vector3(
                lastCalculatedPath[0].x * env.cellSize,
                lastCalculatedPath[0].y * env.cellSize,
                0);

            Gizmos.DrawLine(startPos, firstNodePos);

            for (int i = 0; i < lastCalculatedPath.Count - 1; i++)
            {
                Vector3 p1 = new Vector3(
                    lastCalculatedPath[i].x * env.cellSize,
                    lastCalculatedPath[i].y * env.cellSize,
                    0);
                Vector3 p2 = new Vector3(
                    lastCalculatedPath[i + 1].x * env.cellSize,
                    lastCalculatedPath[i + 1].y * env.cellSize,
                    0);

                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawSphere(p1, 0.15f);
            }
        }

        // Kacýs yolu
        if (escapePath != null && escapePath.Count > 0)
        {
            Gizmos.color = Color.yellow;

            Vector3 startPos = transform.position;
            Vector3 firstNodePos = new Vector3(
                escapePath[0].x * env.cellSize,
                escapePath[0].y * env.cellSize,
                0);

            Gizmos.DrawLine(startPos, firstNodePos);

            for (int i = 0; i < escapePath.Count - 1; i++)
            {
                Vector3 p1 = new Vector3(
                    escapePath[i].x * env.cellSize,
                    escapePath[i].y * env.cellSize,
                    0);
                Vector3 p2 = new Vector3(
                    escapePath[i + 1].x * env.cellSize,
                    escapePath[i + 1].y * env.cellSize,
                    0);

                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawSphere(p1, 0.12f);
            }
        }

        // Ajan tehlikede
        int myX = Mathf.RoundToInt(transform.position.x / env.cellSize);
        int myY = Mathf.RoundToInt(transform.position.y / env.cellSize);

        if (IsInDanger(myX, myY))
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.7f);
            Gizmos.DrawSphere(transform.position, env.cellSize * 0.5f);
        }
    }


}
