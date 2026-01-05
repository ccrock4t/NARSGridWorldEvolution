using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AlpineGridManager;
using static Directions;

public class NARSBody : AgentBody
{
    static IEnumerable<Direction> _directions;
    public NARS nars;



    public NARSBody(NARS nars)
    {
        this.nars = nars;
    }

    public override void Sense(Vector2Int position, AlpineGridManager gridManager)
    {
        if (_directions == null) _directions = Enum.GetValues(typeof(Direction)).Cast<Direction>();
        foreach (var direction in _directions)
        {
            Vector2Int neighbor_pos = position + GetMovementVectorFromDirection(direction);
            if (!IsInBounds(neighbor_pos)) continue;
            var neighbor_type = gridManager.grid[neighbor_pos.x, neighbor_pos.y];
            var sensor_term = GetSensorTermForTileTypeAndDirection(neighbor_type, direction);
            if(sensor_term == null) continue;
            var sensation = new Judgment(this.nars, sensor_term, new(1.0f, 0.99f), nars.current_cycle_number);
            nars.SendInput(sensation);
        }
    }

    public static StatementTerm GetSensorTermForTileTypeAndDirection(TileType type, Direction direction)
    {
        if(type == TileType.Empty)
        {
            return NARSGenome.empty_seen_terms[direction];
        }
        else if(type == TileType.Grass)
        {
            return NARSGenome.grass_seen_terms[direction];
        }
        else if (type == TileType.Berry)
        {
            return NARSGenome.berry_seen_terms[direction];
        }
        //else if (type == TileType.Goat)
        //{
        //    return NARSGenome.goat_seen_terms[direction];
        //}
        else if (type == TileType.Water)
        {
            return NARSGenome.water_seen[direction];
        }
        return null;
    }

 
    public override float GetFitness()
    {
        if(grass_eaten > 0 || berries_eaten > 0)
        {
            return PPOAgent.grassReward*grass_eaten + PPOAgent.berryReward*berries_eaten;
        }
        else
        {
            float move_score = ((float)movement / timesteps_alive);
            return move_score;
        }

    }
}
