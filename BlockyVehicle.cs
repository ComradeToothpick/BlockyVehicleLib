using System;
using System.Collections.Generic;
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
    //private Dictionary<long, IWorldChunk>? _chunks;
    public BlockyVehicle(BlockAccessorBase parent, Vec3d pos, ICoreAPI api, Vec3f? innerPos = null) : base(parent, pos)
    {
        this.innerPos = innerPos ?? Vec3f.Zero;
        this.api = api;
        this.system = api.ModLoader.GetModSystem<BlockyVehicleLibModSystem>();
        //this._chunks = BlockyVehicleLibModSystem.readInternalField<BlockAccessorMovable, Dictionary<long, IWorldChunk>>(
        //    system.Mod.Logger, (BlockAccessorMovable)this, "chunks"
        //);
    }
    /*
    public void LoadChunk(long cindex, ServerChunk chunk)
    {
        //system.AddChunkToLoadedListServer(cindex, chunk);
        base.ReceiveClientChunk(cindex, chunk, api.World);
        base.MarkChunkDirty(
            (int)((cindex & 0x1ff) << 5),
            (int)((cindex >> (42 - 5)) & 0x3ff0),
            (int)((cindex >> (21 - 5)) & 0x3ff0)
        );
    }*/
}