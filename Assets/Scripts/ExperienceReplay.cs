using System.Collections.Generic;
using UnityEngine;

public class Experience
{
    public float[] state;
    public int action;
    public float reward;
    public float[] nextState;
    public bool done;

    public Experience(float[] s, int a, float r, float[] ns, bool d)
    {
        state = s; action = a; reward = r; nextState = ns; done = d;
    }
}

public class ReplayBuffer
{
    private List<Experience> buffer = new List<Experience>();
    private int capacity;

    public ReplayBuffer(int maxCapacity)
    {
        capacity = maxCapacity;
    }

    public void Add(float[] s, int a, float r, float[] ns, bool d)
    {
        if (buffer.Count >= capacity)
        {
            buffer.RemoveAt(0);
        }
        buffer.Add(new Experience(s, a, r, ns, d));
    }

    public List<Experience> Sample(int batchSize)
    {
        List<Experience> batch = new List<Experience>();
        int count = Mathf.Min(batchSize, buffer.Count);

        for (int i = 0; i < count; i++)
        {
            batch.Add(buffer[Random.Range(0, buffer.Count)]);
        }
        return batch;
    }

    public int Count => buffer.Count;
}