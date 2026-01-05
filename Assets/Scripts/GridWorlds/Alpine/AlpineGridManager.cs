using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Mathematics;
using Unity.MLAgents;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEditor;
using UnityEngine;
using static AlpineGridManager;
using static Directions;
using static UnityEngine.EventSystems.EventTrigger;
using Random = UnityEngine.Random;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Actuators;


public class AlpineGridManager : MonoBehaviour
{

    public enum TileType
    {
        Empty,
        Goat,  
        Grass,
        Berry,
        Water
    }

    public enum AIType
    {
        NARS,
        PPO
    }

    [Header("PPO Runtime Spawn")]
    [SerializeField] int ppoAgentCount = 1;
    [SerializeField] string behaviorName = "AlpinePPO";  // MUST match YAML behaviors: key


    [SerializeField]
    GameObject MLAgentsPrefab;

    [NonSerialized]
    public AIType aiType = AIType.PPO;

    public class AIAgent
    {
        public AgentBody body;
    }

    public class NARSAgent : AIAgent
    {
        public NARSGenome genome;
        public NARS nars;

        public NARSAgent()
        {
            genome = new NARSGenome();
            nars = new NARS(genome);
            this.body = new NARSBody(nars);
        }

        public NARSAgent(NARSGenome genome)
        {
            this.genome = genome;
            nars = new NARS(genome);
            this.body = new NARSBody(nars);
        }
    }

    [Header("Grid Settings")]
    public const int width = 40;
    public const int height = 40;

    [Header("Prefabs")]
    public GameObject floorPrefab;
    public GameObject wolfPrefab;
    public GameObject goatPrefab;
    public GameObject ppoAgentPrefab;
    public GameObject grassPrefab;
    public GameObject waterPrefab;

    [Range(0f, 1f)]
    public float randomOpenDensity = 0.10f;

    public TileType[,] grid;
    public GameObject[,] gridGameobjects; // tracks the single occupant for each tile

    Dictionary<AIAgent, Vector2Int> agentToPos = new();
    List<(Vector2Int, AIAgent)> actor_locations = new();

    public static AlpineGridManager Instance;

    public int episode = 0; // PPO episode counter
    public int timestep = 0;
    public float high_score;
    public int largest_genome;

    // --- Tick settings ---
    public const int ENERGY_IN_GRASS = 10;
    public const int ENERGY_IN_BERRY = 20;

    // How many FixedUpdate steps per simulation tick
    private int fixedUpdatesPerSimulationTick = 5;

    private int _fixedUpdateCounter = 0;
    public const int MAX_EPISODE_TIMESTEPS = 100;

    public const int NUM_OF_AGENTS = 1;
    public const int NUM_OF_GRASS = 200;
    public const int NUM_OF_BERRIES = 50;

    int placedGrass = 0;
    int placedBerries = 0;

    AnimatTable table;

    public TMP_Text episodeTXT;
    public TMP_Text timestepTXT;
    public TMP_Text scoreTXT;
    public TMP_Text genomeSizeTXT;

    private string _csvPath;
    private StreamWriter _csv;


    void Awake()
    {
        Instance = this;
        table = new(AnimatTable.SortingRule.sorted,AnimatTable.ScoreType.objective_fitness);
        if (aiType != AIType.NARS)
        {
            var mlagentsGO = Instantiate(MLAgentsPrefab);
        }
    }

    // --- CSV logging ---
    void Start()
    {
       
        GenerateGrid();
        InitCsv();
        if (aiType == AIType.NARS)
        {
            BeginNarsEpisode();

        }
        else
        {
            StartCoroutine(StartPpoRoutine());
        }
    }


    public static bool READY = false;
    System.Collections.IEnumerator StartPpoRoutine()
    {

        yield return MlagentsTrainerLauncher.WaitForTrainerPort(MlagentsTrainerLauncher.BasePort, 20f);
        SpawnPpoAgentsRuntime(); // your method that adds PPOAgent + BehaviorParameters etc.
        READY = true;
    }

    string GetLogRootDirectory()
    {
#if UNITY_EDITOR
        // Editor: project root (one level up from Assets)
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
    // Standalone Win/Linux: parent of "<Game>_Data"
    return Directory.GetParent(Application.dataPath).FullName;
#elif UNITY_STANDALONE_OSX
    // macOS: Application.dataPath = ".../MyGame.app/Contents"
    // Install dir = parent of the .app bundle
    return Directory.GetParent(Application.dataPath).Parent.Parent.FullName;
#else
    // Mobile, WebGL, consoles, etc. — use the safe location
    return Application.persistentDataPath;
#endif
    }
    void InitCsv()
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var root = GetLogRootDirectory();
  

        _csvPath = Path.Combine(root, $"stats_{stamp}.csv");
        _csv = new StreamWriter(_csvPath, false);
        _csv.WriteLine("max_table_score,mean_table_score,median_table_score,generalization_confidence,K");

        _csv.Flush();

        Debug.Log($"CSV logging to: {_csvPath}");
    }

    float GetMedianTableScore()
    {
        int n = table.Count();
        if (n == 0) return 0f;

        // Use the already-sorted table if you're keeping it sorted;
        // otherwise copy & sort scores defensively.
        // Since AnimatTable sorts ascending (lowest at 0), this is O(1) to read.
        // If you ever switch to unsorted, uncomment the defensive path.

        // FAST PATH (sorted table):
        var list = table.table; // public List<TableEntry>
        if ((n & 1) == 1)          // odd
            return list[n / 2].score;
        else                       // even
            return 0.5f * (list[(n / 2) - 1].score + list[n / 2].score);

        // DEFENSIVE PATH (unsorted):
        // var scores = list.Select(e => e.score).ToList();
        // scores.Sort();
        // return (n % 2 == 1) ? scores[n/2]
        //                     : 0.5f * (scores[n/2 - 1] + scores[n/2]);
    }


    void WriteCsvRow()
    {
        if (_csv == null) return;

        // max table score
        float maxTable = 0f;
        var best = table.GetBest();
        if (best.HasValue) maxTable = best.Value.score;

        float total_confidence = 0;
        float total_K = 0;
        foreach(var animat in table.table)
        {
            total_confidence += animat.data.personality_parameters.Generalization_Confidence;
            total_K += animat.data.personality_parameters.k;
        }

        float avg_confidence = 0;
        float avg_K = 0;
        if (table.table.Count > 0)
        {
            avg_confidence = total_confidence / table.table.Count;
            avg_K = total_K / table.table.Count;
        }

        // mean (average) and median
        int count = table.Count();
        float mean = (count > 0) ? (table.total_score / count) : 0f;
        float median = GetMedianTableScore();

        string line = string.Join(",",
            maxTable.ToString(CultureInfo.InvariantCulture),
            mean.ToString(CultureInfo.InvariantCulture),
            median.ToString(CultureInfo.InvariantCulture),
            avg_confidence.ToString(CultureInfo.InvariantCulture),
            avg_K.ToString(CultureInfo.InvariantCulture)
        );

        _csv.WriteLine(line);
        _csv.Flush();
    }


    void OnDestroy()
    {
        if (_csv != null)
        {
            _csv.Flush();
            _csv.Dispose();
            _csv = null;
        }
    }

    void SpawnPpoAgentsRuntime()
    {
        var prefab = ppoAgentPrefab;

        for (int i = 0; i < ppoAgentCount; i++)
        {
            var go = Instantiate(prefab);
            go.name = $"PPOAgent_{i}";

            // 1) BehaviorParameters FIRST
            var bp = go.GetComponent<BehaviorParameters>();
            Debug.Log($"BehaviorType={bp.BehaviorType} BehaviorName={bp.BehaviorName} " +
            $"ActionSpec={bp.BrainParameters.ActionSpec.NumDiscreteActions} branches " +
            $"sizes=({string.Join(",", bp.BrainParameters.ActionSpec.BranchSizes)})");
            Debug.Log($"Model assigned? {(bp.Model != null)}");
            if (bp == null) bp = go.AddComponent<BehaviorParameters>();

            bp.BehaviorName = behaviorName;
            bp.BehaviorType = BehaviorType.Default;

            int dirCount = Directions.CardinalDirs.Length;
            int obsSize = dirCount * 4 + 2;

            bp.BrainParameters.VectorObservationSize = obsSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(new int[]
            {
            dirCount + 1,
            dirCount + 1
            });

            // 2) DecisionRequester next (fine either way, but keep it before agent if possible)
            var dr = go.GetComponent<DecisionRequester>();
            if (dr == null) dr = go.AddComponent<DecisionRequester>();
            dr.DecisionPeriod = 1;
            dr.TakeActionsBetweenDecisions = true;

            // 3) Agent LAST (so Awake sees correct spec)
            var agent = go.GetComponent<PPOAgent>();
            if (agent == null) agent = go.AddComponent<PPOAgent>();
        }
    }



    void OnApplicationQuit()
    {
        OnDestroy(); // ensure it’s closed on quit, too
    }

    public void UpdateUI()
    {
        if (episodeTXT != null) episodeTXT.text = "Episode: " + episode;
        if (timestepTXT != null) timestepTXT.text = "Timestep: " + timestep;
        if (scoreTXT != null) scoreTXT.text = "High Score: " + high_score;
        if (genomeSizeTXT != null) genomeSizeTXT.text = "Largest genome: " + largest_genome;
    }

    public void BeginPpoEpisode()
    {
        episode++;
        timestep = 0;

        Debug.Log($"[PPO] Episode {episode} begin");

        // Update UI immediately so you see the reset
        UpdateUI();
    }

    public void PpoStepTick()
    {
        timestep++;
        if (timestepTXT != null)
            timestepTXT.text = "Timestep: " + timestep;
    }


    void FixedUpdate()
    {
        if (aiType != AIType.NARS) return;
        _fixedUpdateCounter++;

        if (_fixedUpdateCounter >= fixedUpdatesPerSimulationTick)
        {
            _fixedUpdateCounter = 0;   // reset for next tick
            timestep++;

            StepSimulation();

            // ---- Episodic NARS: end conditions + NO respawns mid-episode ----
            if (aiType == AIType.NARS)
            {
                if (agentToPos.Count == 0)
                {
                    EndNarsEpisode("all dead");
                    return;
                }

                if (timestep >= MAX_EPISODE_TIMESTEPS)
                {
                    EndNarsEpisode("time limit");
                    return;
                }
            }
            else
            {
                // ---- Old continuous behavior (keeps densities constant) ----
                int guard = 0;
                while (agentToPos.Count < NUM_OF_AGENTS)
                {
                    if (!SpawnNewAgent()) break;
                    if (++guard > 2000) break;
                }
                guard = 0;
                while (placedGrass < NUM_OF_GRASS)
                {
                    if (!SpawnNewGrass()) break;
                    if (++guard > 2000) break;
                }
                guard = 0;
                while (placedBerries < NUM_OF_BERRIES)
                {
                    if (!SpawnNewBerry()) break;
                    if (++guard > 2000) break;
                }
            }


            UpdateUI();

        }
    }

    private bool SpawnNewBerry()
    {
        if (!TryFindRandomEmptyCell(out var berryPos)) return false;
        if (!PlaceObject(grassPrefab, berryPos, TileType.Berry, Color.red)) return false;
        placedBerries++;
        return true;
    }

    private bool SpawnNewGrass()
    {
        if (!TryFindRandomEmptyCell(out var grassPos)) return false;
        if (!PlaceObject(grassPrefab, grassPos, TileType.Grass)) return false;
        placedGrass++;
        return true;
    }

    private bool SpawnNewAgent()
    {
        // New agent was placed?
        bool placedAny = false;

        int newagent = UnityEngine.Random.Range(0, 50);
        if (newagent == 0 || table.Count() < 2)
        {
            if (!TryFindRandomEmptyCell(out var pos)) return false;
            if (!PlaceObject(goatPrefab, pos, TileType.Goat)) return false;

            var agent = new NARSAgent();
            agentToPos.Add(agent, pos);
            placedAny = true;
        }
        else
        {
            int sexual = UnityEngine.Random.Range(0, 2);
            NARSGenome[] new_genomes = GetNewAnimatReproducedFromTable(sexual == 1);

            foreach (var genome in new_genomes)
            {
                if (!TryFindRandomEmptyCell(out var pos)) return placedAny; // no more room
                if (!PlaceObject(goatPrefab, pos, TileType.Goat)) continue;

                var agent = new NARSAgent(genome);
                agentToPos.Add(agent, pos);
                placedAny = true;
            }
        }
        return placedAny;
    }

    bool IsCellEmpty(Vector2Int p)
    {
        return IsInBounds(p)
            && grid[p.x, p.y] == TileType.Empty
            && gridGameobjects[p.x, p.y] == null;
    }

    bool TryFindRandomEmptyCell(out Vector2Int pos, int maxAttempts = 2000)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            var candidate = new Vector2Int(Random.Range(0, width), Random.Range(0, height));
            if (IsCellEmpty(candidate))
            {
                pos = candidate;
                return true;
            }
        }
        // Fallback: linear scan in case the grid is crowded
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var p = new Vector2Int(x, y);
                if (IsCellEmpty(p))
                {
                    pos = p;
                    return true;
                }
            }

        pos = default;
        return false;
    }

    bool PlaceObject(GameObject prefab, Vector2Int gridPos, TileType type, Color? tint = null)
    {
        if (!IsCellEmpty(gridPos)) return false;

        var obj = Instantiate(prefab, new Vector3(gridPos.x, gridPos.y, 0f), Quaternion.identity);

        if (tint.HasValue)
            ApplyTint(obj, tint.Value);

        gridGameobjects[gridPos.x, gridPos.y] = obj;
        grid[gridPos.x, gridPos.y] = type;
        return true;
    }


    // --------------------------------------------------
    //  GRID CREATION
    // --------------------------------------------------
    void GenerateGrid()
    {
        grid = new TileType[width, height];
        gridGameobjects = new GameObject[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Instantiate(floorPrefab, new Vector3(x, y, 0), Quaternion.identity);
                grid[x, y] = TileType.Empty;
            }
        }
    }

    // --------------------------------------------------
    //  MAIN LAYOUT
    // --------------------------------------------------



    // --------------------------------------------------
    //  SIMULATION STEP
    // --------------------------------------------------
    void StepSimulation()
    {
        // Collect current positions of all animals (wolves + goats).
        actor_locations.Clear();
        var keys = agentToPos.Keys.ToArray();
        foreach (var key in keys)
        {
            var agent_pos = agentToPos[key];
            var agent = key;

            if (agent.body.energy <= 0 || agent.body.remaining_life <= 0)
            {
                KillAgent(agent,agent_pos);
            }
            else
            {
                actor_locations.Add((agent_pos, agent));
                if(agent is NARSAgent narsAgent)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        agent.body.Sense(agent_pos, this);
                        // enter instinctual goals
                        foreach (var goal_data in narsAgent.nars.genome.goals)
                        {
                            var goal = new Goal(narsAgent.nars, goal_data.statement, goal_data.evidence, occurrence_time: narsAgent.nars.current_cycle_number);
                            narsAgent.nars.SendInput(goal);
                        }
                        narsAgent.nars.do_working_cycle();
                    }
                }


                agent.body.timesteps_alive++;
                agent.body.energy--;
                agent.body.remaining_life--;

            }
        }   
 

        // Shuffle so movement order is random each tick (reduces bias).
        FisherYatesShuffle(actor_locations);


        // Try to move each actor one step into a random valid neighboring cell.
        foreach (var actor in actor_locations)
        {
            // If something already moved out/in earlier this tick, skip if empty now.
            var fromLocation = actor.Item1;
            var agent = actor.Item2;
            var type = grid[fromLocation.x, fromLocation.y];
            if (!IsAgent(type)) continue;

            if (agent is NARSAgent narsAgent)
            {
                NARSMotorStep(fromLocation, narsAgent);
            }

        }
    }

    void NARSMotorStep(Vector2Int fromLocation, NARSAgent narsAgent)
    {
        // try eat
        Direction? dirtoeat = null;
        float max_eat_activation = 0.0f;

        foreach (var kvp in NARSGenome.eat_op_terms)
        {
            var eatTerm = kvp.Value;
            float activation = narsAgent.nars.GetGoalActivation(eatTerm);
            if (activation < narsAgent.nars.config.T) continue;
            if (activation > max_eat_activation)
            {
                dirtoeat = kvp.Key;
            }

        }

        if (dirtoeat != null)
        {
            var eatLocation = fromLocation + GetMovementVectorFromDirection((Direction)dirtoeat);
            TryEatEntity(narsAgent, eatLocation, out _);
        }

        // try moves
        Direction? dirtomove = null;
        float max_move_activation = 0.0f;

        foreach (var kvp in NARSGenome.move_op_terms)
        {
            var moveTerm = kvp.Value;
            float activation = narsAgent.nars.GetGoalActivation(moveTerm);
            if (activation < narsAgent.nars.config.T) continue;
            if (activation > max_move_activation)
            {
                dirtomove = kvp.Key;
                max_move_activation = activation;
            }
        }

        if (dirtomove != null)
        {
            var toLocation = fromLocation + GetMovementVectorFromDirection((Direction)dirtomove);
            MoveEntity(fromLocation, toLocation, narsAgent);
        }
    }


    void KillAgent(AIAgent agent, Vector2Int agentPos)
    {

        grid[agentPos.x, agentPos.y] = TileType.Empty;

        if(agent is NARSAgent narsAgent)
        {
            float score = agent.body.GetFitness();
            table.TryAdd(score, narsAgent.genome);
            if (score > high_score)
            {
                high_score = score;
                UpdateUI();
            }

            if (narsAgent.genome.beliefs.Count > largest_genome)
            {
                largest_genome = narsAgent.genome.beliefs.Count;
                UpdateUI();
            }

        }

        ClearTileAt(agentPos);
        agentToPos.Remove(agent);
    }

    public static Vector2Int GetMovementVectorFromDirection(Direction dirtomove)
    {
        return CardinalDirs[(int)dirtomove];
    }


    public bool IsAgent(TileType t)
    {
        return (t == TileType.Goat);
    }
    public bool TryEatEntity(AIAgent agent, Vector2Int eatLocation, out TileType eatenType)
    {
        eatenType = TileType.Empty;

        if (!IsInBounds(eatLocation)) return false;

        var type = grid[eatLocation.x, eatLocation.y];
        if (type != TileType.Grass && type != TileType.Berry)
            return false;

        ClearTileAt(eatLocation);

        if (type == TileType.Grass)
        {
            agent.body.energy = ENERGY_IN_GRASS;
            placedGrass--;
        }
        else
        {
            agent.body.energy = ENERGY_IN_BERRY;
            placedBerries--;
        }

        agent.body.food_eaten++;
        eatenType = type;
        return true;
    }


    public bool MoveEntity(Vector2Int from, Vector2Int to, AIAgent agent)
    {
        if (!IsInBounds(to)) return false;
        if (to == from) return false;

        var to_type = grid[to.x, to.y];
        if (IsBlocked(to_type))
            return false;

        var from_type = grid[from.x, from.y];

        agent.body.movement++;

        var obj = gridGameobjects[from.x, from.y];
        if (obj == null)
            return false;

        obj.transform.position = new Vector3(to.x, to.y, 0f);

        gridGameobjects[to.x, to.y] = obj;
        gridGameobjects[from.x, from.y] = null;

        grid[to.x, to.y] = from_type;
        grid[from.x, from.y] = TileType.Empty;

        // Only update agentToPos if this is a NARS agent tracked in the dict
        if (agentToPos.ContainsKey(agent))
            agentToPos[agent] = to;

        return true;
    }

    public void ClearCellWithoutDestroy(Vector2Int pos)
    {
        if (!IsInBounds(pos)) return;

        gridGameobjects[pos.x, pos.y] = null;
        grid[pos.x, pos.y] = TileType.Empty;
    }

    public void PlaceExistingAgentObject(GameObject agentGO, Vector2Int pos, TileType type)
    {
        if (!IsInBounds(pos)) return;

        gridGameobjects[pos.x, pos.y] = agentGO;
        grid[pos.x, pos.y] = type;
        agentGO.transform.position = new Vector3(pos.x, pos.y, 0f);
    }

    public void BeginNarsEpisode()
    {
        episode++;
        timestep = 0;

        Debug.Log($"[NARS] Episode {episode} begin");
        UpdateUI();

        // Reset the environment (destroy all non-floor occupants)
        ResetNarsEpisodeLayout();

        // Spawn a fresh episode population + resources
        int guard = 0;

        while (placedGrass < NUM_OF_GRASS)
        {
            if (!SpawnNewGrass()) break;
            if (++guard > 5000) break;
        }

        guard = 0;
        while (placedBerries < NUM_OF_BERRIES)
        {
            if (!SpawnNewBerry()) break;
            if (++guard > 5000) break;
        }

        int placedWater = 0;
        guard = 0;
        while (placedWater < 100 && TryFindRandomEmptyCell(out var waterPos))
        {
            if (PlaceObject(waterPrefab, waterPos, TileType.Water)) placedWater++;
            if (++guard > 5000) break;
        }

        guard = 0;
        while (agentToPos.Count < NUM_OF_AGENTS)
        {
            if (!SpawnOneNarsAgent()) break;
            if (++guard > 5000) break;
        }

        UpdateUI();
    }
    public void EndNarsEpisode(string reason)
    {
        Debug.Log($"[NARS] Episode {episode} end ({reason}) | timestep={timestep} | alive={agentToPos.Count}");

        // Score/record survivors too (otherwise time-limit episodes never add to table)
        var keys = agentToPos.Keys.ToArray();
        foreach (var a in keys)
        {
            var p = agentToPos[a];
            KillAgent(a, p);
        }

        // Log ONE row per episode (table now includes this episode's results)
        WriteCsvRow();

        // Start next episode immediately
        BeginNarsEpisode();
    }

    // Clears everything except the floor (since floor isn't tracked in gridGameobjects)
    void ResetNarsEpisodeLayout()
    {
        // Destroy all occupants
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (gridGameobjects[x, y] != null)
                {
                    Destroy(gridGameobjects[x, y]);
                    gridGameobjects[x, y] = null;
                }
                grid[x, y] = TileType.Empty;
            }
        }

        agentToPos.Clear();
        actor_locations.Clear();

        placedGrass = 0;
        placedBerries = 0;
    }

    bool SpawnOneNarsAgent()
    {
        if (!TryFindRandomEmptyCell(out var pos)) return false;
        if (!PlaceObject(goatPrefab, pos, TileType.Goat)) return false;

        NARSGenome genome;

        int newagent = UnityEngine.Random.Range(0, 50);
        if (newagent == 0 || table.Count() < 2)
        {
            genome = new NARSGenome();
        }
        else
        {
            bool sexual = UnityEngine.Random.Range(0, 2) == 1;
            var offspring = GetNewAnimatReproducedFromTable(sexual);
            genome = offspring[0]; // ensure we only add ONE agent
        }

        var agent = new NARSAgent(genome);
        agentToPos.Add(agent, pos);
        return true;
    }


    public void ResetEpisodeLayout(GameObject agentGO)
    {
        // Clear all non-floor occupants, but do not destroy the agent GO
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var obj = gridGameobjects[x, y];
                if (obj != null && obj != agentGO)
                    Destroy(obj);

                gridGameobjects[x, y] = null;
                grid[x, y] = TileType.Empty;
            }
        }

        placedGrass = 0;
        placedBerries = 0;

        // Re-spawn a fresh layout
        int placedWater = 0;

        while (placedGrass < NUM_OF_GRASS && TryFindRandomEmptyCell(out var grassPos))
            if (PlaceObject(grassPrefab, grassPos, TileType.Grass)) placedGrass++;

        while (placedBerries < NUM_OF_BERRIES && TryFindRandomEmptyCell(out var berryPos))
            if (PlaceObject(grassPrefab, berryPos, TileType.Berry, Color.red)) placedBerries++;

        while (placedWater < 100 && TryFindRandomEmptyCell(out var waterPos))
            if (PlaceObject(waterPrefab, waterPos, TileType.Water)) placedWater++;
    }


    public bool IsBlocked(TileType type)
    {
        return type == TileType.Water
            || type == TileType.Goat
            || type == TileType.Grass
            || type == TileType.Berry;
    }


    public static bool IsInBounds(Vector2Int p)
    {
        return p.x >= 0 && p.x < width && p.y >= 0 && p.y < height;
    }

    void FisherYatesShuffle<T>(IList<T> list)
    {
        for (int i = 0; i < list.Count - 1; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // --------------------------------------------------
    //  TILE REMOVAL / REPLACEMENT
    // --------------------------------------------------
    public void ClearTileAt(Vector2Int pos)
    {
        if (gridGameobjects[pos.x, pos.y] != null)
        {
            Destroy(gridGameobjects[pos.x, pos.y]);
            gridGameobjects[pos.x, pos.y] = null;
        }
        grid[pos.x, pos.y] = TileType.Empty;
    }

    // --------------------------------------------------
    //  OBJECT PLACEMENT
    // --------------------------------------------------

    NARSGenome[] GetNewAnimatReproducedFromTable(bool sexual)
    {
        (NARSGenome parent1, int parent1_idx) = table.PeekProbabilistic();

        NARSGenome[] results;
        if (sexual)
        {
            results = new NARSGenome[2];
            // sexual
            int ignore_idx = -1;
            ignore_idx = parent1_idx; // same table, so dont pick the same animat
            (NARSGenome parent2, int parent2_idx) = table.PeekProbabilistic(ignore_idx: ignore_idx);

            NARSGenome offspring1_genome;
            NARSGenome offspring2_genome;
            (offspring1_genome, offspring2_genome) = parent1.Reproduce(parent2);

            results[0] = offspring1_genome;
            results[1] = offspring2_genome;
        }
        else
        {
            results = new NARSGenome[1];
            // asexual
            NARSGenome cloned_genome = parent1.Clone();
            cloned_genome.Mutate();
            results[0] = cloned_genome;
        }
        return results;
    }

    public static string GetRandomDirectionString()
    {
        Array values = Enum.GetValues(typeof(Direction));
        int index = Random.Range(0, values.Length);  // using UnityEngine.Random
        Direction randomDir = (Direction)values.GetValue(index);
        return randomDir.ToString();
    }

    void ApplyTint(GameObject obj, Color tint)
    {
        // 2D sprites
        var sr = obj.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.color = tint;
            return;
        }

        // 3D renderer (use MPB so you don't permanently modify shared material)
        var r = obj.GetComponent<Renderer>();
        if (r == null) return;

        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);

        // common shader color properties
        if (r.sharedMaterial != null)
        {
            if (r.sharedMaterial.HasProperty("_Color")) mpb.SetColor("_Color", tint);
            if (r.sharedMaterial.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", tint);
        }

        r.SetPropertyBlock(mpb);
    }
    // AlpineGridManager.cs
    public void ReportPpoEpisodeResult(float cumulativeReward)
    {
        if (cumulativeReward > high_score)
            high_score = cumulativeReward;

        UpdateUI();
    }

}
