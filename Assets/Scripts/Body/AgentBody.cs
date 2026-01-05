using UnityEngine;
using static AlpineGridManager;

public abstract class AgentBody
{
    public int energy = ENERGY_IN_GRASS;
    public int grass_eaten = 0;
    public int berries_eaten = 0;
    public int movement = 0;
    public const int MAX_LIFE = 100;
    public int remaining_life = MAX_LIFE;
    public int timesteps_alive = 0;

    public abstract void Sense(Vector2Int position, AlpineGridManager gridManager);

    public abstract float GetFitness();
}
