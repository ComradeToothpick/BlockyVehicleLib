using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace BlockyVehicleLib;

public class BlockyVehicle : BlockAccessorMovable, IMiniDimension
{
    ICoreAPI api;
    private BlockyVehicleLibModSystem system;
    public Vec3f innerPos;
    protected override bool SetSolidBlock(int blockId, BlockPos pos, IWorldChunk chunk, ItemStack byItemstack)
    {
        return base.SetSolidBlock(blockId, pos, chunk, byItemstack);
    }

    protected override void AddToCenterOfMass(Block block, BlockPos pos, int sign)
    {
        base.AddToCenterOfMass(block, pos, sign);
    }

    //private Dictionary<long, IWorldChunk>? _chunks;
    public BlockyVehicle(BlockAccessorBase parent, Vec3d pos, ICoreAPI api, Vec3f? innerPos = null) : base(parent, pos)
    {
        this.innerPos = innerPos ?? Vec3f.Zero;
        this.api = api;
        this.TrackSelection = true;
        this.system = api.ModLoader.GetModSystem<BlockyVehicleLibModSystem>();
        //this._chunks = BlockyVehicleLibModSystem.readInternalField<BlockAccessorMovable, Dictionary<long, IWorldChunk>>(
        //    system.Mod.Logger, (BlockAccessorMovable)this, "chunks"
        //);
    }
    
    public void AdjustPosForSubDimension(ref BlockPos pos)
    {
        pos.X += subDimensionId % 4096 * 16384 + 8192;
        pos.Y += 8192;
        pos.Z += subDimensionId / 4096 * 16384 + 8192;
    }
    
    public override FastVec3d GetRenderOffset(float dt)
    {
        FastVec3d fastVec3d = new FastVec3d(-(subDimensionId % 4096) * 16384, 0.0, -(subDimensionId / 4096 * 16384));
        fastVec3d = fastVec3d.Add(-8192.0);
        return fastVec3d.Add(CurrentPos.X - 0.5, CurrentPos.InternalY, CurrentPos.Z - 0.5);
    }

    public bool CallSetSolidBlock(
        int blockId,
        BlockPos pos,
        IWorldChunk chunk,
        ItemStack byItemstack)
    {
        return SetSolidBlock(blockId, pos, chunk, byItemstack);
    }
}