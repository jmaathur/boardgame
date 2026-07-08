using System.Collections.Generic;
using BoardGame.Core.Generated;

namespace BoardGame.Core.Sim
{
    /// <summary>A battle-time member entity (one of a squad's N bodies).</summary>
    public sealed class MemberState
    {
        public int Index;
        public double X;
        public double Z;
        public double Hp;
        public double MaxHp;
        public double Shield;
        public bool Alive => Hp > 0;

        // Weapon cooldowns, one per weapon on the unit (ticks until next fire).
        public int[] WeaponCooldown = System.Array.Empty<int>();

        // Sticky target (squad + member index), -1 when none.
        public int TargetSquad = -1;
        public int TargetMember = -1;

        // Active statuses: statusId → ticks remaining (int.MaxValue = aura-refreshed).
        public readonly Dictionary<string, int> Statuses = new Dictionary<string, int>();

        // Whether this member moved since the last keyframe (for moved-only logs).
        public bool MovedSinceKeyframe;
    }

    /// <summary>A squad: one card's worth of members owned by a seat.</summary>
    public sealed class SquadState
    {
        public int BattleSquadId;
        public int Seat;
        public UnitDef Unit = null!;
        public int Level;
        /// <summary>Coin invested (for survivor valuation); 0 for synthetic spawns.</summary>
        public int Invested;
        /// <summary>Card id this squad came from (null for mid-battle spawns).</summary>
        public string? CardId;

        public readonly List<MemberState> Members = new List<MemberState>();

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var m in Members) if (m.Alive) n++;
                return n;
            }
        }

        public bool AnyAlive => AliveCount > 0;
    }

    /// <summary>One army going into battle (a seat's blueprint).</summary>
    public sealed class ArmyBlueprint
    {
        public int Seat;
        public readonly List<SquadBlueprint> Squads = new List<SquadBlueprint>();
    }

    public sealed class SquadBlueprint
    {
        public string UnitId = "";
        public int AnchorRow;
        public int AnchorCol;
        public Orientation Orientation;
        public int Level = 1;
        public int Invested;
        public string? CardId;
    }
}
