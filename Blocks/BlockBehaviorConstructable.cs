using System.Text;
using BlockyVehicleLib.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BlockyVehicleLib.Blocks;

public class BlockBehaviorConstructable : BlockBehaviorReinforcable
{
    public BlockBehaviorConstructable(Block block) : base(block)
    {
    }
    
    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
    {
        if (byPlayer == null) return;  // Fast return path for no player (although normally OnBlockBroken will specify a player)
        ModSystemBlockReinforcement modBre;
        modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        BlockReinforcement bre = modBre.GetReinforcment(pos);
        ModSystemBlockConstruction modBco;
        modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        BlockConstruction bco = modBco.GetConstruction(pos);
        
        if ((bre != null && bre.Strength > 0) || (bco != null && bco.Strength > 0))
        {
            handling = EnumHandling.PreventDefault;   // This prevents the block from breaking normally, while it any amount of reinforcement left

            world.PlaySoundAt(new AssetLocation("sounds/tool/breakreinforced"), pos, 0, byPlayer);

            if (!byPlayer.HasPrivilege("denybreakreinforced"))//Should remove construction before reinforcement
            {
                if (bco != null && bco.Strength > 0)
                {
                    modBco.ConsumeStrength(pos, 1);
                }
                else if (bre != null && bre.Strength > 0)
                {
                    modBre.ConsumeStrength(pos, 1);
                }
                
                world.BlockAccessor.MarkBlockDirty(pos);
            }
        }
    }
    
    public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, ref EnumHandling handling)
    {
        ModSystemBlockReinforcement modBre;
        modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        BlockReinforcement bre = modBre.GetReinforcment(pos);
        ModSystemBlockConstruction modBco;
        modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        BlockConstruction bco = modBco.GetConstruction(pos);
        
        if (bco != null && bco.Strength > 0)
        {
            modBco.ConsumeStrength(pos, 2);
            world.BlockAccessor.MarkBlockDirty(pos);
            handling = EnumHandling.PreventDefault;
            return;
        }
        if (bre != null && bre.Strength > 0)
        {
            modBre.ConsumeStrength(pos, 2);
            world.BlockAccessor.MarkBlockDirty(pos);
            handling = EnumHandling.PreventDefault;
            return;
        }

        base.OnBlockExploded(world, pos, explosionCenter, blastType, ref handling);
    }
    
    public override float GetMiningSpeedModifier(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        ModSystemBlockReinforcement modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        BlockReinforcement bre = modBre.GetReinforcment(pos);
        ModSystemBlockConstruction modBco;
        modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        BlockConstruction bco = modBco.GetConstruction(pos);
        if (((bre != null && bre.Strength > 0) || (bco != null && bco.Strength > 0)) && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            return 0.6f;
        }
        return 1.0f;
    }
    
    public override void OnBlockPlaced(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
    {
        if (world.Side == EnumAppSide.Server)
        {
            // Clear any existing reinforcement or construction

            ModSystemBlockReinforcement modBre;
            modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
            modBre.ClearReinforcement(pos);
            ModSystemBlockConstruction modBco;
            modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
            modBco.ClearConstruction(pos);
        }
    }
    
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        ModSystemBlockReinforcement modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        ModSystemBlockConstruction modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();

        StringBuilder sb = new StringBuilder();
        
        if (modBre != null)
        {
            BlockReinforcement bre = modBre.GetReinforcment(pos);
            if (bre == null) return null;

            if (bre.GroupUid != 0)
            {
                sb.AppendLine(Lang.Get(bre.Locked ? "Has been locked and reinforced by group {0}." : "Has been reinforced by group {0}.", bre.LastGroupname));
            } else
            {
                sb.AppendLine(Lang.Get(bre.Locked ? "Has been locked and reinforced by {0}." : "Has been reinforced by {0}.", bre.LastPlayername));
            }

            sb.AppendLine(Lang.Get("Reinforcement Strength: {0}", bre.Strength));
        }
        if (modBco != null)
        {
            BlockConstruction bco = modBco.GetConstruction(pos);
            if (bco == null) return null;
            
            if (bco.GroupUid != 0)
            {
                sb.AppendLine(Lang.Get("Has been constructed by group {0}.", bco.LastGroupname));
            }
            else
            {
                sb.AppendLine(Lang.Get("Has been constructed by {0}.", bco.LastPlayername));
            }

            sb.AppendLine(Lang.Get("Construction Strength: {0}", bco.Strength));
        }
        if (sb.ToString() != string.Empty) return sb.ToString();
        return null;
    }


    /// <summary>
    /// Prevent right-click pickup in survival mode, for blocks which have any level of construction on them
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="byPlayer"></param>
    /// <returns>True if pickup is allowed; false if pickup is denied</returns>
    static public bool AllowRightClickPickup(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        ModSystemBlockConstruction modBco;
        ModSystemBlockReinforcement modBre;

        modBre = world.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        BlockReinforcement bre = modBre.GetReinforcment(pos);
        modBco = world.Api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        BlockConstruction bco = modBco.GetConstruction(pos);

        if ((bre != null && bre.Strength > 0) || (bco != null && bco.Strength > 0))
        {
            return false;
        }
        return true;
    }
}