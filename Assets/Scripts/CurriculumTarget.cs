using UnityEngine;
using System.Collections.Generic;

public class CurriculumTarget : MonoBehaviour
{
    private QBombENV_sc env;

    [Header("Faz Ayarlari")]
    public int currentPhase = 1;
    public int phase1Duration = 300; 

    [Header("Istatistikler")]
    public int totalEpisodes = 0;
    public int currentPhaseEpisodes = 0;

    void Start()
    {
        env = FindObjectOfType<QBombENV_sc>();
    }

    public void OnTargetStep()
    {
        if (env == null || !env.isAlive || env.kill) return;

        if (currentPhase == 1)
        {
            return;
        }
        else
        {
            MoveRandomly();
        }
    }

    void MoveRandomly()
    {
        List<int> validMoves = new List<int>();

        if (IsValidMove(0)) validMoves.Add(0); //Yukari
        if (IsValidMove(1)) validMoves.Add(1); // Asagi
        if (IsValidMove(2)) validMoves.Add(2); // Sag
        if (IsValidMove(3)) validMoves.Add(3); // Sol

        // Rastgele ilerie
        if (validMoves.Count > 0)
        {
            int randomAction = validMoves[Random.Range(0, validMoves.Count)];
            ExecuteMove(randomAction);
        }
    }

    void ExecuteMove(int action)
    {
        int newX = env.targetX;
        int newY = env.targetY;

        if (action == 0) newY++;
        else if (action == 1) newY--;
        else if (action == 2) newX++;
        else if (action == 3) newX--;

        // Konumu güncelle
        if (IsValidPos(newX, newY))
        {
            env.targetX = newX;
            env.targetY = newY;
            transform.position = new Vector3(newX * env.cellSize, newY * env.cellSize, 0);
        }
    }

    bool IsValidMove(int action)
    {
        int nx = env.targetX;
        int ny = env.targetY;
        if (action == 0) ny++; if (action == 1) ny--;
        if (action == 2) nx++; if (action == 3) nx--;
        return IsValidPos(nx, ny);
    }

    bool IsValidPos(int x, int y)
    {
        if (x < 0 || x >= env.width || y < 0 || y >= env.height) return false;
        if (env.map[x, y] != 0) return false;
        return true;
    }

    public void OnEpisodeEnd()
    {
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
            Debug.Log($"=== TARGET FAZ 2'ye Geçti (Rastgele Hareket) - Total Ep: {totalEpisodes} ===");
        }
    }
}