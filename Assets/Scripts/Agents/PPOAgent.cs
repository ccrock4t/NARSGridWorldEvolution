using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using static AlpineGridManager;
using static Directions;

using MLAgent = Unity.MLAgents.Agent;

public class PPOAgent : MLAgent
{
    [SerializeField] AlpineGridManager grid;

    // This is the "grid agent" wrapper so we can reuse MoveEntity/TryEatEntity
    private AlpineGridManager.AIAgent gridAgent;

    private Vector2Int pos;

    // Per-step shaping (tune later)
    [SerializeField] float stepPenalty = 0.001f;
    [SerializeField] float deathPenalty = 1.0f;

    [SerializeField] float grassReward = 1.0f;
    [SerializeField] float berryReward = 2.0f;

    private int DirCount => CardinalDirs.Length;

    public override void Initialize()
    {
        if (grid == null)
            grid = AlpineGridManager.Instance;

        grid.aiType = AlpineGridManager.AIType.PPO;

        gridAgent = new AlpineGridManager.AIAgent();
        gridAgent.body = new PPOBody();
    }

    public override void OnEpisodeBegin()
    {
        grid.BeginPpoEpisode();

        // Reset internal body state (explicit episode)
        gridAgent.body.energy = AlpineGridManager.ENERGY_IN_GRASS;
        gridAgent.body.food_eaten = 0;
        gridAgent.body.movement = 0;
        gridAgent.body.remaining_life = AgentBody.MAX_LIFE;
        gridAgent.body.timesteps_alive = 0;

        // Reset the environment layout
        grid.ResetEpisodeLayout(this.gameObject);

        // Clear our previous cell without destroying this GO
        grid.ClearCellWithoutDestroy(pos);

        // Place the agent into a fresh empty cell
        if (!TryFindSpawn(out pos))
            pos = new Vector2Int(0, 0);

        grid.PlaceExistingAgentObject(this.gameObject, pos, TileType.Goat);
    }

    bool TryFindSpawn(out Vector2Int spawn)
    {
        // Use the same logic as grid manager: random empty cell
        // We can’t call its private TryFindRandomEmptyCell; so do a simple random scan here.
        for (int i = 0; i < 2000; i++)
        {
            var p = new Vector2Int(Random.Range(0, AlpineGridManager.width), Random.Range(0, AlpineGridManager.height));
            if (AlpineGridManager.IsInBounds(p)
                && grid.grid[p.x, p.y] == TileType.Empty
                && grid.gridGameobjects[p.x, p.y] == null)
            {
                spawn = p;
                return true;
            }
        }
        spawn = default;
        return false;
    }
    private bool loggedObsOnce = false;
    public override void CollectObservations(VectorSensor sensor)
    {
        if (!loggedObsOnce)
        {
            Debug.Log("CollectObservations is being called.");
            loggedObsOnce = true;
        }
        // NARS-like: for each direction, indicate presence of each non-empty type.
        // Order: Grass, Berry, Water, Goat
        for (int d = 0; d < DirCount; d++)
        {
            var npos = pos + CardinalDirs[d];

            TileType t = TileType.Water; // treat OOB as Water (blocked)
            if (AlpineGridManager.IsInBounds(npos))
                t = grid.grid[npos.x, npos.y];

            sensor.AddObservation(t == TileType.Grass ? 1f : 0f);
            sensor.AddObservation(t == TileType.Berry ? 1f : 0f);
            sensor.AddObservation(t == TileType.Water ? 1f : 0f);
            sensor.AddObservation(t == TileType.Goat ? 1f : 0f);
        }

        // Internal state (analogous to “instinct pressure”)
        sensor.AddObservation(Mathf.Clamp01((float)gridAgent.body.energy / AlpineGridManager.ENERGY_IN_BERRY));
        sensor.AddObservation(Mathf.Clamp01((float)gridAgent.body.remaining_life / AgentBody.MAX_LIFE));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Discrete branch layout:
        // branch 0: eatDir (0..DirCount-1, DirCount = no-op)
        // branch 1: moveDir (0..DirCount-1, DirCount = no-op)
        int eatIdx = actions.DiscreteActions[0];
        int moveIdx = actions.DiscreteActions[1];

        // Eat first (matches NARSMotorStep)
        if (eatIdx >= 0 && eatIdx < DirCount)
        {
            var eatLoc = pos + CardinalDirs[eatIdx];
            if (grid.TryEatEntity(gridAgent, eatLoc, out var eatenType))
            {
                if (eatenType == TileType.Grass) AddReward(grassReward);
                else if (eatenType == TileType.Berry) AddReward(berryReward);
            }
        }

        // Then move
        if (moveIdx >= 0 && moveIdx < DirCount)
        {
            var toLoc = pos + CardinalDirs[moveIdx];
            if (grid.MoveEntity(pos, toLoc, gridAgent))
                pos = toLoc;
        }

        // Tick body state (since we disabled GridManager.FixedUpdate sim in PPO mode)
        gridAgent.body.timesteps_alive++;
        gridAgent.body.energy--;
        gridAgent.body.remaining_life--;

        AddReward(-stepPenalty);

        if (gridAgent.body.energy <= 0 || gridAgent.body.remaining_life <= 0 || gridAgent.body.timesteps_alive > AlpineGridManager.MAX_EPISODE_TIMESTEPS)
        {
            AddReward(-deathPenalty);
            EndEpisode();
        }

        grid.PpoStepTick();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Optional: manual control for debugging
        var da = actionsOut.DiscreteActions;
        da[0] = DirCount; // no eat
        da[1] = DirCount; // no move
    }
}
