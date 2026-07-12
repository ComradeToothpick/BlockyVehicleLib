using System.Collections.Generic;
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
        [ProtoMember(1)] public int index;
        [ProtoMember(2)] public int vehicleWandID;
    }
    [ProtoContract]
    public class DimensionSpawnRequest
    {
        [ProtoMember(1)] public required int dimensionIndex;
        [ProtoMember(2)] public required Vec3d pos;
        [ProtoMember(3)] public BlockSelection blockSel;
        [ProtoMember(4)] public int blockId;
        [ProtoMember(5)] public EnumVehicleMode mode;
    }
    
    [ProtoContract]
    public class DimensionSpawnClientResponse
    {
        [ProtoMember(1)] public int dimId;
        [ProtoMember(2)] public BlockPos blockPos;
        [ProtoMember(3)] public Vec3d vecPos;
        [ProtoMember(4)] public int blockId;
    }
    
    [ProtoContract]
    public class DimensionSpawnClientComplete
    {
        [ProtoMember(1)] public bool success;
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
    
    [ProtoContract]
    public class BlockConstruction
    {
        [ProtoMember(1)]public int Strength;
        [ProtoMember(2)]public string PlayerUID;
        [ProtoMember(3)]public string LastPlayername;
        [ProtoMember(4)]public bool Locked;
        [ProtoMember(5)]public string LockedByItemCode;
        [ProtoMember(6)]public int GroupUid;
        [ProtoMember(7)]public string LastGroupname;
    }
    
    [ProtoContract]
    public class ConstructedPrivilegeGrants
    {
        [ProtoMember(1)]public string OwnedByPlayerUid;
        [ProtoMember(2)]public int OwnedByGroupId;
        [ProtoMember(3)]public Dictionary<string, EnumBlockAccessFlags> PlayerGrants = new Dictionary<string, EnumBlockAccessFlags>();
        [ProtoMember(4)]public Dictionary<int, EnumBlockAccessFlags> GroupGrants = new Dictionary<int, EnumBlockAccessFlags>();
    }
    
    [ProtoContract]
    public class ConstructedPrivilegeGrantsGroup
    {
        [ProtoMember(1)]public string OwnedByPlayerUid;
        [ProtoMember(2)]public int OwnedByGroupId;
        [ProtoMember(3)]public EnumBlockAccessFlags DefaultGrants = EnumBlockAccessFlags.BuildOrBreak | EnumBlockAccessFlags.Use;
        [ProtoMember(4)]public Dictionary<string, EnumBlockAccessFlags> PlayerGrants = new Dictionary<string, EnumBlockAccessFlags>();
        [ProtoMember(5)]public Dictionary<int, EnumBlockAccessFlags> GroupGrants = new Dictionary<int, EnumBlockAccessFlags>();
    }
}
