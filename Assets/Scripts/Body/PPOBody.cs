using UnityEngine;
using static AlpineGridManager;

public class PPOBody : AgentBody
{
    public override void Sense(Vector2Int position, AlpineGridManager gridManager)
    {
        // PPO observations are collected in the ML-Agents AIAgent script (CollectObservations).
    }

    public override float GetFitness()
    {
        return grass_eaten;
    }
}
