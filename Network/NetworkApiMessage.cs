using ProtoBuf;
using BlockyVehicleLib.Items;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace BlockyVehicleLib.Network
{
    [ProtoContract]
    public class DimensionIndexRequest
    {
        [ProtoMember(1)]
        public string playerName;
        [ProtoMember(2)]
        public int vehicleWandID;
    }
    [ProtoContract]
    public class DimensionIndexResponse
    {
        [ProtoMember(1)]
        public int index;
        [ProtoMember(2)]
        public int vehicleWandID;
    }
    [ProtoContract]
    public class DimensionSpawnRequest
    {
        [ProtoMember(1)]
        public required int dimensionIndex;
        [ProtoMember(2)]
        public required Vec3d pos;
        [ProtoMember(3)]
        public BlockSelection blockSel;
        [ProtoMember(4)] public int blockId;
    }
    
    [ProtoContract]
    public class DimensionSpawnClientResponse
    {
        [ProtoMember(1)]
        public int dimId;

        [ProtoMember(2)] public BlockPos blockPos;
        
        [ProtoMember(3)] public Vec3d vecPos;
        
        [ProtoMember(4)] public int blockId;
    }
    
    [ProtoContract]
    public class DimensionSpawnClientComplete
    {
        [ProtoMember(1)]
        public bool success;
    }

    [ProtoContract]
    public class VehicleEntityId
    {
        [ProtoMember(1)] public long entityId;
        [ProtoMember(2)] public int subDimensionId;
    }

    [ProtoContract]
    public class VehicleBlocks
    {
        [ProtoMember(1)] public required int[] blockIds;
        [ProtoMember(2)] public required BlockPos[] localPos;
        [ProtoMember(3)] public required int dimId;
        [ProtoMember(4)] public required long entityId;
        [ProtoMember(5)] public required Vec3d CoM;
    }
    
    [ProtoContract]
    public class ChunkConstructionData
    {
        [ProtoMember(1)] public byte[] Data;
        [ProtoMember(2)] public int chunkX;
        [ProtoMember(3)] public int chunkY;
        [ProtoMember(4)] public int chunkZ;
    }
    
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class BlockReinforcement
    {
        public int Strength;
        public string PlayerUID;
        public string LastPlayername;
        public bool Locked;
        public string LockedByItemCode;
        public int GroupUid;
        public string LastGroupname;
    }
}
