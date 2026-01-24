using UnityEngine;
using System;
using System.IO;

[System.Serializable]
public class SimpleMLP
{
    public int inputSize;
    public int hiddenSize;
    public int outputSize;
    public float learningRate;
    public float momentum = 0.9f;

    public float weightDecay = 0.0001f;

    public float[,] weightsIH;
    public float[,] weightsHO;
    public float[] biasH;
    public float[] biasO;

    // Momentum hýzlarý
    private float[,] velocityIH;
    private float[,] velocityHO;
    private float[] velocityBiasH;
    private float[] velocityBiasO;

    private float[] hiddenLayerOutput;

    public SimpleMLP(int inp, int hid, int outSize, float lr)
    {
        inputSize = inp;
        hiddenSize = hid;
        outputSize = outSize;
        learningRate = lr;

        weightsIH = new float[inputSize, hiddenSize];
        weightsHO = new float[hiddenSize, outputSize];
        biasH = new float[hiddenSize];
        biasO = new float[outputSize];

        velocityIH = new float[inputSize, hiddenSize];
        velocityHO = new float[hiddenSize, outputSize];
        velocityBiasH = new float[hiddenSize];
        velocityBiasO = new float[outputSize];

        InitializeWeights();
    }

    void InitializeWeights()
    {
        UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);
        float limitIH = Mathf.Sqrt(6.0f / (inputSize + hiddenSize));
        float limitHO = Mathf.Sqrt(6.0f / (hiddenSize + outputSize));

        for (int i = 0; i < inputSize; i++)
            for (int j = 0; j < hiddenSize; j++)
                weightsIH[i, j] = UnityEngine.Random.Range(-limitIH, limitIH);

        for (int i = 0; i < hiddenSize; i++)
            for (int j = 0; j < outputSize; j++)
                weightsHO[i, j] = UnityEngine.Random.Range(-limitHO, limitHO);
    }

    public float[] Forward(float[] inputs)
    {
        hiddenLayerOutput = new float[hiddenSize];
        float[] outputLayer = new float[outputSize];

        for (int h = 0; h < hiddenSize; h++)
        {
            float sum = 0;
            for (int i = 0; i < inputSize; i++)
                sum += inputs[i] * weightsIH[i, h];
            sum += biasH[h];
            hiddenLayerOutput[h] = ReLU(sum);
        }

        for (int o = 0; o < outputSize; o++)
        {
            float sum = 0;
            for (int h = 0; h < hiddenSize; h++)
                sum += hiddenLayerOutput[h] * weightsHO[h, o];
            sum += biasO[o];
            outputLayer[o] = sum;
        }
        return outputLayer;
    }

    public void Train(float[] inputs, float[] targetOutputs)
    {
        float[] predictedOutputs = Forward(inputs);

        // Output Delta
        float[] outputDeltas = new float[outputSize];
        for (int o = 0; o < outputSize; o++)
        {
            float error = targetOutputs[o] - predictedOutputs[o];
            outputDeltas[o] = Mathf.Clamp(error, -100f, 100f);
        }

        // Hidden Delta
        float[] hiddenDeltas = new float[hiddenSize];
        for (int h = 0; h < hiddenSize; h++)
        {
            float error = 0;
            for (int o = 0; o < outputSize; o++)
                error += outputDeltas[o] * weightsHO[h, o];
            hiddenDeltas[h] = error * ReLUDerivative(hiddenLayerOutput[h]);
        }

        // Hidden -> Output
        for (int h = 0; h < hiddenSize; h++)
        {
            for (int o = 0; o < outputSize; o++)
            {
                float gradient = outputDeltas[o] * hiddenLayerOutput[h];

                // Weight Decay (-weightDecay * currentWeight)
                // Agirliðin asiri buyumesini engellemek icin
                velocityHO[h, o] = (momentum * velocityHO[h, o]) + (learningRate * gradient) - (learningRate * weightDecay * weightsHO[h, o]);
                weightsHO[h, o] += velocityHO[h, o];
            }
        }

        // Output Bias
        for (int o = 0; o < outputSize; o++)
        {
            velocityBiasO[o] = (momentum * velocityBiasO[o]) + (learningRate * outputDeltas[o]);
            biasO[o] += velocityBiasO[o];
        }

        // Input -> Hidden
        for (int i = 0; i < inputSize; i++)
        {
            for (int h = 0; h < hiddenSize; h++)
            {
                float gradient = hiddenDeltas[h] * inputs[i];

                // Weight Decay
                velocityIH[i, h] = (momentum * velocityIH[i, h]) + (learningRate * gradient) - (learningRate * weightDecay * weightsIH[i, h]);
                weightsIH[i, h] += velocityIH[i, h];
            }
        }

        // Hidden Bias
        for (int h = 0; h < hiddenSize; h++)
        {
            velocityBiasH[h] = (momentum * velocityBiasH[h]) + (learningRate * hiddenDeltas[h]);
            biasH[h] += velocityBiasH[h];
        }
    }

    float ReLU(float x) => Math.Max(0, x);
    float ReLUDerivative(float x) => x > 0 ? 1 : 0;

    [System.Serializable]
    private class ModelData
    {
        public int inputSize, hiddenSize, outputSize;
        public float[] flatWeightsIH;
        public float[] flatWeightsHO;
        public float[] biasH;
        public float[] biasO;
    }

    public void SaveModel(string path)
    {
        ModelData data = new ModelData();
        data.inputSize = inputSize; data.hiddenSize = hiddenSize; data.outputSize = outputSize;
        data.biasH = biasH; data.biasO = biasO;

        data.flatWeightsIH = new float[inputSize * hiddenSize];
        for (int i = 0; i < inputSize; i++) for (int j = 0; j < hiddenSize; j++) data.flatWeightsIH[i * hiddenSize + j] = weightsIH[i, j];

        data.flatWeightsHO = new float[hiddenSize * outputSize];
        for (int i = 0; i < hiddenSize; i++) for (int j = 0; j < outputSize; j++) data.flatWeightsHO[i * outputSize + j] = weightsHO[i, j];

        File.WriteAllText(path, JsonUtility.ToJson(data));
        Debug.Log("Model saved: " + path);
    }

    public void LoadModel(string path)
    {
        if (!File.Exists(path)) return;
        ModelData data = JsonUtility.FromJson<ModelData>(File.ReadAllText(path));

        if (data.inputSize != inputSize || data.hiddenSize != hiddenSize || data.outputSize != outputSize) return;

        biasH = data.biasH; biasO = data.biasO;
        for (int i = 0; i < inputSize; i++) for (int j = 0; j < hiddenSize; j++) weightsIH[i, j] = data.flatWeightsIH[i * hiddenSize + j];
        for (int i = 0; i < hiddenSize; i++) for (int j = 0; j < outputSize; j++) weightsHO[i, j] = data.flatWeightsHO[i * outputSize + j];

        // Velocity sifirlama
        Array.Clear(velocityIH, 0, velocityIH.Length);
        Array.Clear(velocityHO, 0, velocityHO.Length);
        Array.Clear(velocityBiasH, 0, velocityBiasH.Length);
        Array.Clear(velocityBiasO, 0, velocityBiasO.Length);
    }
}