using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using BlockyVehicleLib.Items;
using BlockyVehicleLib.Entities;
using BlockyVehicleLib.Network;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace BlockyVehicleLib;

public class BlockyVehicleLibModSystem : ModSystem
{
    private ICoreAPI api;
    public ICoreServerAPI sapi;
    public ICoreClientAPI capi;
    private Dictionary<string, int> _dimensionRegistry = null!;
    private Dictionary<int, BlockyVehicle> _loadedMinidimensions = new Dictionary<int, BlockyVehicle>();
    private Dictionary<int, long> _loadedEntityVehicles = new Dictionary<int, long>();
    private int _dimensionIndex = -1;
    private bool _spawnSuccess = false;
    private Harmony harmony;

    public override void StartPre(ICoreAPI coreApi)
    {
        api = coreApi;
    }
    
    public override void Start(ICoreAPI coreApi)
    {
        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
        
        api.Logger.Event(Mod.Info.ModID + ".vehicle");
        api.RegisterEntity(Mod.Info.ModID + ".vehicle", typeof(EntityVehicle));
        api.RegisterItemClass(Mod.Info.ModID + ".vehiclewand", typeof(ItemVehicleWand));
        api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".vehiclephysics", typeof(BehaviorPassivePhysicsVehicle));
        api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".vehiclekinematiccollider", typeof(BehaviorKinematicVehicleCollider));
        api.Network
            .RegisterChannel("VehicleNetworkApi")
            .RegisterMessageType<DimensionIndexRequest>()
            .RegisterMessageType<DimensionIndexResponse>()
            .RegisterMessageType<DimensionSpawnRequest>()
            .RegisterMessageType<DimensionSpawnClientResponse>()
            .RegisterMessageType<DimensionSpawnClientComplete>()
            .RegisterMessageType<VehicleEntityId>()
            .RegisterMessageType<VehicleBlocks>();
    }

    IServerNetworkChannel serverChannel;
    
    public override void StartServerSide(ICoreServerAPI serverApi)
    {
        sapi = serverApi;
        
        sapi.Event.SaveGameLoaded += OnSaveGameLoaded;
        sapi.Event.GameWorldSave += OnGameWorldSave;
        sapi.Event.PlayerJoin += OnPlayerJoin;
        
        serverChannel = sapi.Network
            .GetChannel("VehicleNetworkApi")
            .SetMessageHandler<DimensionIndexRequest>(OnDimensionIndexRequest)
            .SetMessageHandler<DimensionSpawnRequest>(OnDimensionSpawnRequest)
            .SetMessageHandler<DimensionSpawnClientComplete>(OnDimensionSpawnClientComplete);
        
        /***COMMANDS***/
        
        var rootCommand = api.ChatCommands.Create("bv")
            .WithDescription("Root command for editing, managing, and deleting block based vehicles.")
            .RequiresPrivilege(Privilege.gamemode)
            .RequiresPlayer();
        
        var subCommandDelete = rootCommand.BeginSubCommand("delete")//No aliases to prevent accidental deletion
            .WithDescription(
                "Deletes a vehicle and all its contents with the given vehicle ID")
            .WithArgs(api.ChatCommands.Parsers
                .Unparsed("vehicleId"))
            .HandleWith((args) =>
            {
                return new TextCommandResult();
            });
        
        var subCommandCreate = rootCommand.BeginSubCommand("create")
            .WithDescription(
                "Makes a new vehicle")
            .WithArgs(api.ChatCommands.Parsers
                .Unparsed("vehicleId"))
            .HandleWith((args) =>
            {
                //Should be able to use selected volume, or the block the user is looking at (either checking adjacent blocks for construction or just the block individually)
                return new TextCommandResult();
            });
        
        var subCommandList = rootCommand.BeginSubCommand("list")
            .WithDescription(
                "Lists all vehicles, or all vehicles owned by a given player")
            .WithArgs(api.ChatCommands.Parsers
                .Unparsed("vehicleId"))
            .HandleWith((args) =>
            {
                //Should accept player names and list the vehicles that player owns or "no player found"
                //Could accept faction names to list all vehicles owned by members of the faction
                //Could accept multiple names in one command
                //Could accept region names to list all vehicles in a given region (depends on implementation of region names)
                //Could accept a pair of coordinates or a coordinate and range to do similar to the above
                
                return new TextCommandResult();
            });
        
        var subCommandTeleport = rootCommand.BeginSubCommand("teleport")//How should rotation and passengers be handled?
            .WithDescription(
                "Teleports a vehicle with the given vehicle ID to the given coordinate")
            .WithArgs(api.ChatCommands.Parsers
                .Unparsed("vehicleId"))
            .HandleWith((args) =>
            {
                return new TextCommandResult();
            });
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        if(api.Side != EnumAppSide.Server) return;
        EntityProperties? playerEntity = api.World.GetEntityType(new AssetLocation("game", "player"));
        if (playerEntity == null)
        {
            api.Logger.Error("Could not find player entity");
            return;
        }
    }

    public void OnDimensionIndexRequest(IServerPlayer player, DimensionIndexRequest message)
    {
        int index = GetMiniDimensionPlayerIndex(player);
        ((ItemVehicleWand)api.World.Items[message.vehicleWandID]).DimensionIndex = index;
        serverChannel.SendPacket(new DimensionIndexResponse() { index = index, vehicleWandID = message.vehicleWandID}, player);
    }
    
    public int GetMiniDimensionPlayerIndex(IPlayer player)
    {
        if (api.Side == EnumAppSide.Server)
        {
            _dimensionIndex = -1;
            foreach (var ele in _dimensionRegistry)
            {
                if (ele.Key == player.PlayerUID)
                {
                    _dimensionIndex = ele.Value;
                }
            }
            int output = _dimensionIndex;
            _dimensionIndex = -1;
            return output;
        }
        return -1;
    }

    IClientNetworkChannel clientChannel;
    
    public override void StartClientSide(ICoreClientAPI clientApi)
    {
        capi = clientApi;

        clientChannel = capi.Network.GetChannel("VehicleNetworkApi")
            .SetMessageHandler<DimensionIndexResponse>(OnDimensionIndexResponse)
            .SetMessageHandler<DimensionSpawnClientResponse>(OnDimensionSpawnClientResponse)
            .SetMessageHandler<VehicleEntityId>(OnVehicleEntityId)
            .SetMessageHandler<VehicleBlocks>(BuildVehicleColliders);
    }
    
    private void OnDimensionIndexResponse(DimensionIndexResponse message)
    {
        //api.Logger.Event("BlockyVehicleLibModSystem.OnDimensionIndexResponse (client side): " + message.index);
        ((ItemVehicleWand)api.World.Items[message.vehicleWandID]).DimensionIndex = message.index;
    }

    public async void OnDimensionSpawnRequest(IPlayer player, DimensionSpawnRequest message)
    {
        if (api.Side == EnumAppSide.Client) return;
        
        //What should happen here?
        //Find the minidimension associated with the given index
        //Clear the minidimension (A warning should be given to the player first)
        //unload the unused server chunks
        //Place the blocks into the minidimension
        //spawn the entity
        //associate the minidimension with the entity
        //Send a packet to all nearby players to get them to do the clientside of this
        
        IMiniDimension? messageDim = sapi.Server.GetMiniDimension(message.dimensionIndex);
        BlockyVehicle dim;
        bool loadedDim = false;
        //set the loaded minidimension to the correct index (should be unnecessary in current state, but will keep for future proofing)
        BlockPos pos = message.blockSel.Position.Copy();
        
        IPlayer[] players = sapi.Server.Players;
        if (messageDim == null)
        {
            dim = new BlockyVehicle((BlockAccessorBase)sapi.World.BlockAccessor, pos.ToVec3d(), sapi);
            sapi.Server.SetMiniDimension(dim, message.dimensionIndex);
            _loadedMinidimensions.Add(message.dimensionIndex, dim);
        }
        else
        {
            if (_loadedMinidimensions.ContainsKey(message.dimensionIndex))
            {
                dim = _loadedMinidimensions[message.dimensionIndex];
                loadedDim = true;
            }
            else
            {
                dim = new BlockyVehicle((BlockAccessorBase)sapi.World.BlockAccessor, pos.ToVec3d(), sapi);
                _loadedMinidimensions.Add(message.dimensionIndex, dim);
            }
            sapi.Server.SetMiniDimension(dim, message.dimensionIndex);
            dim.CurrentPos.SetPos(pos); //repeat this on client side
        }
        
        dim.SetSubDimensionId(message.dimensionIndex);
        BlockPos dimPos = new BlockPos(new Vec3i(0, 0, 0), 1);
        
        dim.AdjustPosForSubDimension(ref dimPos);
        BlockPos localOrigin = dimPos.CopyAndCorrectDimension();
        dim.ClearChunks();
        int blockId = message.blockId;
        DebugBuild(blockId, dim, dimPos, localOrigin, out int[] blockIds, out BlockPos[] localPos);
        dim.RecalculateCenterOfMass(api.World);
        Cuboidf[] initialColliders = CollectBlocks(blockIds, localPos, dim.subDimensionId, dim.CenterOfMass);
        EntityVehicle entity;
        if (loadedDim && _loadedEntityVehicles.TryGetValue(message.dimensionIndex, out long entityId))
        {
            entity = (EntityVehicle)sapi.World.GetEntityById(entityId);
        }
        else
        {
            entity = EntityVehicle.CreateVehicle(sapi, dim, initialColliders);
            sapi.World.SpawnEntity(entity);
            _loadedEntityVehicles.TryAdd(message.dimensionIndex, entity.EntityId);
        }
        
        entity.Pos.SetPos(pos.X + 0.5f, pos.Y + 1f, pos.Z + 0.5f);
        dim.CurrentPos.SetPos(entity.Pos);
        EntityVehicle.InitializeVehicle(entity, dim, initialColliders);//re-initialize to fix desync issue
        
        serverChannel.SendPacket(new DimensionSpawnClientResponse() {dimId = dim.subDimensionId, blockPos = pos, vecPos = message.pos, blockId = message.blockId}, (IServerPlayer) player);
        await WaitingOnClient();
        serverChannel.SendPacket(new VehicleBlocks() {blockIds =  blockIds, localPos = localPos, dimId = dim.subDimensionId, entityId = entity.EntityId, CoM = dim.CenterOfMass}, (IServerPlayer) player);
        
        dim.CollectChunksForSending(players);
    }

    public void DebugBuild(int blockId, BlockyVehicle dim, BlockPos pos, BlockPos localOrigin, out int[] blockIds, out BlockPos[] localPos)
    {
        blockIds = new int[20];
        localPos = new BlockPos[20];
        for (int i = 0; i < 10; i++)//Staircase formation
        {
            dim.SetBlock(blockId, pos, new ItemStack());
            blockIds[2 * i] = dim.GetBlock(pos).BlockId;
            localPos[2 * i] = new BlockPos(pos.Z - localOrigin.Z, pos.Y  - localOrigin.Y, pos.X - localOrigin.X);
            pos.X += 1;
            dim.SetBlock(blockId, pos, new ItemStack());
            blockIds[2 * i + 1] = dim.GetBlock(pos).BlockId;
            localPos[2 * i + 1] = new BlockPos(pos.Z - localOrigin.Z, pos.Y  - localOrigin.Y, pos.X - localOrigin.X);
            pos.Y += 1;
        }
    }

    public Cuboidf[]? CollectBlocks(int[] blockIds, BlockPos[] localPos, int dimId, Vec3d CoM)
    {
        BlockyVehicle dim;
        if (api.Side == EnumAppSide.Client)
        {
            dim = (BlockyVehicle)((ICoreClientAPI)api).World.MiniDimensions[dimId];
        }
        else
        {
            try
            {
                dim = _loadedMinidimensions[dimId];
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
                
            }
            
        }
        //localPos is relative to the entity position
        //needs to be converted to be relative to CoM
        Vec3f[] vecLocalPos = new Vec3f[localPos.Length];
        for (int i = 0; i < localPos.Length; i++)
        {
            vecLocalPos[i] = new Vec3f();//Swapping x and z to correct orientation
            vecLocalPos[i].X = (float) (localPos[i].X - CoM.X);
            vecLocalPos[i].Y = (float) (localPos[i].Y - CoM.Y);
            vecLocalPos[i].Z = (float) (localPos[i].Z - CoM.Z);
        }
        
        List<Cuboidf> boxList = new List<Cuboidf>();
        for (int i = 0; i < blockIds.Length; i++)
        {
            Block block = dim.GetBlock(blockIds[i]);
            int degY = 0;
            if (block.Code.Path.Contains("-north-"))
            {
                degY = 0;
            }
            if (block.Code.Path.Contains("-west-"))
            {
                degY = 90;
            }

            if (block.Code.Path.Contains("-south-"))
            {
                degY = 180;
            }

            if (block.Code.Path.Contains("-east-"))
            {
                degY = 270;
            }
            Cuboidf[] colliders = block.CollisionBoxes;
            foreach (Cuboidf collider in colliders)
            {
                Cuboidf collider2 = collider.RotatedCopy(0, degY, 0, new Vec3d(0.5, 0.5, 0.5));
                collider2.X1 = (float)Math.Round(collider2.X1 + localPos[i].X, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Y1 = (float)Math.Round(collider2.Y1 + localPos[i].Y, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Z1 = (float)Math.Round(collider2.Z1 + localPos[i].Z, 2, MidpointRounding.ToPositiveInfinity);
                collider2.X2 = (float)Math.Round(collider2.X2 + localPos[i].X, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Y2 = (float)Math.Round(collider2.Y2 + localPos[i].Y, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Z2 = (float)Math.Round(collider2.Z2 + localPos[i].Z, 2, MidpointRounding.ToPositiveInfinity);
                boxList.Add(collider2);
            }
        }
        return boxList.ToArray();
    }

    /*private void OnDimensionSpawnClientResponse(DimensionSpawnClientResponse message)
    {
        if (api.Side != EnumAppSide.Client) return;
        BlockyVehicle dim = new BlockyVehicle((BlockAccessorBase)capi.World.BlockAccessor, message.vecPos, capi);
        dim.SetSubDimensionId(message.dimId);
        capi.World.MiniDimensions[message.dimId] = dim;
        //IMiniDimension dim = capi.World.GetOrCreateDimension(message.dimId, message.vecPos);
        Cuboidf[] initialColliders = CollectBlocks(message.blockIds, message.blockPos, dim.subDimensionId, dim.CenterOfMass);
        Vec3d newPos = message.vecPos.Add(new Vec3f(0.5f, 1.0f, 0.5f));
        dim.CurrentPos.SetPos(newPos);
        dim.selectionTrackingOriginalPos = dim.CenterOfMass.AsBlockPos;//Need to set this for it to render in the world
        EntityVehicle entity = EntityVehicle.CreateVehicle(capi, dim, initialColliders);
        capi.World.SpawnEntity(entity);
        capi.Network.GetChannel("VehicleNetworkApi").SendPacket<DimensionSpawnClientComplete>(new DimensionSpawnClientComplete() {success = true});
    }*/
    
    private void OnDimensionSpawnClientComplete(IPlayer player, DimensionSpawnClientComplete message)
    {
        _spawnSuccess = message.success;
    }

    private async Task WaitingOnClient()
    {
        int i = 0;
        while (!_spawnSuccess && i < 1000)
        {
            i++;
            await Task.Delay(1);
        }
        _spawnSuccess = false;
    }
    
    private void OnPlayerJoin(IPlayer player)
    {
        if (api.Side == EnumAppSide.Server)
        {
            int dimIndex = GetMiniDimensionPlayerIndex(player);
            if (dimIndex == -1)
            {
                BlockAccessorMovable dim = (BlockAccessorMovable) sapi.World.BlockAccessor.CreateMiniDimension(new Vec3d(0, 0, 0));
                int index = sapi.Server.LoadMiniDimension(dim);
                _dimensionRegistry.Add(player.PlayerUID, index);
            }
        }
    }

    private void OnSaveGameLoaded()
    {
        byte[] data = sapi.WorldManager.SaveGame.GetData("Vehicle.DimensionRegistry");
        _dimensionRegistry = data == null ? new Dictionary<string, int>() : SerializerUtil.Deserialize<Dictionary<string, int>>(data);
    }

    private void OnGameWorldSave()
    {
        sapi.WorldManager.SaveGame.StoreData("Vehicle.DimensionRegistry", SerializerUtil.Serialize(_dimensionRegistry));
    }
    
    public override void Dispose() 
    {
        harmony.UnpatchAll(Mod.Info.ModID);
    }
}