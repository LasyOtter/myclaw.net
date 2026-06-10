namespace MyClaw.Core.VectorMemory;

/// <summary>
/// 向量相似度计算的共享实现。
/// </summary>
internal static class VectorMath
{
    /// <summary>
    /// 计算两个向量的余弦相似度。
    ///
    /// 前置不变量：进入向量库的向量都来自 <see cref="SimpleEmbeddingService"/>，
    /// 在嵌入阶段已做 L2 归一化（单位向量）。对单位向量而言
    /// cos(a,b) = (a·b)/(‖a‖‖b‖) = a·b，因此直接返回点积即可，
    /// 省去每次比较时对双边模长各做一次 <c>Math.Sqrt</c>（在 N 条候选上是热路径）。
    ///
    /// 空白文本会得到零向量，其点积自然为 0，与原 <c>magnitude == 0 → 0</c> 行为一致。
    /// </summary>
    public static double CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length || vector1.Length == 0)
            return 0;

        double dotProduct = 0;
        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
        }

        return dotProduct;
    }
}
