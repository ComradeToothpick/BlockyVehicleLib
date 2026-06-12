using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    //private Harmony harmony;
    
    

    //Each player only gets one minidimension (for now)
    //Will change this later, mostly to support singleplayer

    public override void StartPre(ICoreAPI coreApi)
    {
        api = coreApi;
    }
    
    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI coreApi)
    {
        //harmony patching not necessary yet
        //harmony = new Harmony(Mod.Info.ModID);
        //harmony.PatchAll();
        
        api.Logger.Event(Mod.Info.ModID + ".vehicle");
        api.RegisterEntity(Mod.Info.ModID + ".vehicle", typeof(EntityVehicle));
        //api.RegisterEntityClass(Mod.Info.ModID + ".entities.vehicle", typeof(EntityVehicle));
        api.RegisterItemClass(Mod.Info.ModID + ".vehiclewand", typeof(ItemVehicleWand));
        api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".basevehiclephysics", typeof(PhysicsBehaviorBaseVehicle));
        api.RegisterEntityBehaviorClass("blockyvehiclelib.entityvehiclephysics", typeof(EntityBehaviorVehiclePhysics));
        api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".vehiclephysics", typeof(VehicleBehaviourVehiclePhysics));
        //api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".vehiclephysicsbehavior", typeof(BVLBehaviorVehiclePhysics));
        api.Network
            .RegisterChannel("VehicleNetworkApi")
            .RegisterMessageType<DimensionIndexRequest>()
            .RegisterMessageType<DimensionIndexResponse>()
            .RegisterMessageType<DimensionSpawnRequest>()
            .RegisterMessageType<DimensionSpawnClientResponse>()
            .RegisterMessageType<DimensionSpawnClientComplete>()
            .RegisterMessageType<VehicleEntityId>();
    }

    IServerNetworkChannel serverChannel;
    
    public override void StartServerSide(ICoreServerAPI serverApi)
    {
        sapi = serverApi;
        //_dimensionRegistry = new Dictionary<string, int>();
        //IMiniDimension dim = serverApi.World.BlockAccessor.CreateMiniDimension(new Vec3d(0, 0, 0));
        //int index = serverApi.Server.LoadMiniDimension(dim);
        //EntityChunky entity = EntityVehicle.CreateVehicle(serverApi, dim);
        
        sapi.Event.SaveGameLoaded += OnSaveGameLoaded;
        sapi.Event.GameWorldSave += OnGameWorldSave;
        sapi.Event.PlayerJoin += OnPlayerJoin;
        //sapi.Event.PlayerCreate += OnPlayerCreate;
        
        serverChannel = sapi.Network
            .GetChannel("VehicleNetworkApi")
            .SetMessageHandler<DimensionIndexRequest>(OnDimensionIndexRequest)
            .SetMessageHandler<DimensionSpawnRequest>(OnDimensionSpawnRequest)
            .SetMessageHandler<DimensionSpawnClientComplete>(OnDimensionSpawnClientComplete);
        
        //Mod.Logger.Notification("Mini dimension loaded, index: " + index);
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
        var BVLbehaviors = new List<JsonObject>(1);//Behavior adding code courtesy of HoD authors Chronolegionaire and TheInsanityGod

        //Forcibly insert behaviors to ensure they are present
        BVLbehaviors.Add(new(new JObject { ["code"] =  "blockyvehiclelib.entityvehiclephysics" }));

        playerEntity.Server.BehaviorsAsJsonObj = [
            ..playerEntity.Server.BehaviorsAsJsonObj,
            ..BVLbehaviors
        ];
        
        playerEntity.Client.BehaviorsAsJsonObj = [
            ..playerEntity.Client.BehaviorsAsJsonObj,
            ..BVLbehaviors
        ];
    }

    public void OnDimensionIndexRequest(IServerPlayer player, DimensionIndexRequest message)
    {
        api.Logger.Event("BlockyVehicleLibModSystem.OnDimensionIndexResponse (server side): " + message.playerName);
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
            .SetMessageHandler<VehicleEntityId>(OnVehicleEntityId);
        //Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("Vehicle:hello"));
    }
    
    private void OnVehicleEntityId(VehicleEntityId message)
    {
        //send the EntityId to the client.player.playerentity.EntityBehaviourVehiclePhysics.collisionTester
        capi.World.Player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>().AddVehicle(message.entityId, message.subDimensionId);
    }

    
    private void OnDimensionIndexResponse(DimensionIndexResponse message)
    {
        api.Logger.Event("BlockyVehicleLibModSystem.OnDimensionIndexResponse (client side): " + message.index);
        ((ItemVehicleWand)api.World.Items[message.vehicleWandID]).DimensionIndex = message.index;
    }

    public async void OnDimensionSpawnRequest(IPlayer player, DimensionSpawnRequest message)
    {
        if (api.Side == EnumAppSide.Client) return;
        
        //What should happen here?
        //Find the minidimension associated with the given index
        //Clear the minidimension (A warning should be given to the player first)
        //unload the unused server chunks
        //spawn the entity
        //associate the minidimension with the entity
        //Send a packet to all nearby players to get them to do the clientside of this
        //Place the blocks into the minidimension
        //Will need a more rigourous way to place blocks in the minidimension once more than one block are involved
        
        IMiniDimension? messageDim = sapi.Server.GetMiniDimension(message.dimensionIndex);
        BlockyVehicle dim;
        bool loadedDim = false;
        //set the loaded minidimension to the correct index (should be unnecessary in current state, but will keep for future proofing)
        BlockPos pos = message.blockSel.Position.Copy();
        
        if (messageDim == null)
        {
            dim = new BlockyVehicle((BlockAccessorBase)sapi.World.BlockAccessor, pos.ToVec3d(), sapi);
            sapi.Server.SetMiniDimension(dim, message.dimensionIndex);//this needs to be fixed
            _loadedMinidimensions.Add(message.dimensionIndex, dim);
            sapi.Logger.Error("message not found, new dimension created");
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
                sapi.Logger.Error("Mini dimension not found, new dimension created");
            }
            sapi.Server.SetMiniDimension(dim, message.dimensionIndex);
            dim.CurrentPos.SetPos(pos); //repeat this on client side
        }
        
        dim.SetSubDimensionId(message.dimensionIndex);
        BlockPos pos2 = new BlockPos(new Vec3i(0, 0, 0), 1);
        //pos.Sub(message.blockSel.Position);
        //pos.SetDimension(1);
        pos2.X +=
            (int)(message.dimensionIndex % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
        
        pos2.Y += 8192 /*0x2000*/;
        
        pos2.Z +=
            (int)(message.dimensionIndex / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
        
        dim.ClearChunks();
        //create the entity and associate it with the minidimension
        //or find the entity if it already exists and move it.
        //Doing it this way stops the rotation
        EntityVehicle entity;
        if (loadedDim && _loadedEntityVehicles.TryGetValue(message.dimensionIndex, out long entityId))
        {
            entity = (EntityVehicle)sapi.World.GetEntityById(entityId);
        }
        else
        {
            entity = EntityVehicle.CreateVehicle(sapi, dim);
            sapi.World.SpawnEntity(entity);
            _loadedEntityVehicles.TryAdd(message.dimensionIndex, entity.EntityId);
        }

        entity.Pos.SetPos(pos.X + 0.5f, pos.Y + 1f, pos.Z + 0.5f);
        dim.CurrentPos.SetPos(entity.Pos);
        serverChannel.SendPacket(new DimensionSpawnClientResponse() {dimId = dim.subDimensionId, blockPos = pos, vecPos = message.pos, blockId = message.blockId}, (IServerPlayer) player);
        await WaitingOnClient();
        //Do these after client side
        int blockId = message.blockId;
        dim.SetBlock(blockId, pos2, BlockLayersAccess.Solid);
        api.Logger.Event("Block ID: " + dim.GetBlockId(pos2));
        //sapi.World.BlockAccessor.SetBlock(blockId, pos2, 0);
        //dim.UnloadUnusedServerChunks();
        IPlayer[] players = sapi.Server.Players;
        dim.CollectChunksForSending(players);
        api.Logger.Event("Vehicle Spawned Successfully");
    }
    /*
    internal static T? readInternalField<O, T>(ILogger logger, O obj, string fieldName)
    {
        if (obj == null)
        {
            logger.Error("{0}.{1} Cannot read internal field of null", typeof(O).Name, fieldName);
            return default(T);
        }

        FieldInfo? field = typeof(O).GetField(
            fieldName,
            // Include public in case they change it to be public
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
        );
        if (field == null)
        {
            logger.Error("{0}.{1} does not exist", typeof(O).Name, fieldName);
            return default(T);
        }
        if (field.IsPublic)
        {
            logger.Warning("{0}.{1} is public, reflection is no longer needed", typeof(O).Name, fieldName);
        }
        object? val = field.GetValue(obj);
        if (val == null)
        {
            logger.Warning("{0}.{1} is null", typeof(O).Name, fieldName);
            return default(T);
        }
        if (!val.GetType().IsAssignableTo(typeof(T)))
        {
            logger.Error("{0}.{1} has an unexpected type: {2} that is not assignable to {3}", typeof(O).Name, fieldName, val.GetType(), typeof(T));
            return default(T);
        }
        return (T)val;
    }*/

    private void OnDimensionSpawnClientResponse(DimensionSpawnClientResponse message)
    {
        if (api.Side == EnumAppSide.Client)
        {
            BlockyVehicle dim = new BlockyVehicle((BlockAccessorBase)capi.World.BlockAccessor, message.vecPos, capi);
            capi.World.MiniDimensions[message.dimId] = dim;
            dim.SetSubDimensionId(message.dimId);
            //IMiniDimension dim = capi.World.GetOrCreateDimension(message.dimId, message.vecPos);
            
            Vec3d newPos = message.vecPos.Add(new Vec3f(0.5f, 1.0f, 0.5f));
            //dim.CurrentPos.SetPos(newPos);
            dim.selectionTrackingOriginalPos = message.blockPos;//Need to set this for it to render in the world
            dim.selectionTrackingOriginalPos.Y += 1;
            capi.World.SpawnEntity(EntityVehicle.CreateVehicle(capi, dim));
            clientChannel.SendPacket(new DimensionSpawnClientComplete() {success = true});
        }
    }
    
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
            player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>().entity = (Entity) player.Entity;
            //if (player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>() == null) sapi.Logger.Event("Behavior not found");
            //else sapi.Logger.Event("Behavior found");
            //This testing revealed the behavior is getting added successfully.
            //So why is it doing nothing?
            int dimIndex = GetMiniDimensionPlayerIndex(player);
            if (dimIndex == -1)
            {
                IMiniDimension dim = sapi.World.BlockAccessor.CreateMiniDimension(new Vec3d(0, 0, 0));
                int index = sapi.Server.LoadMiniDimension(dim);
                _dimensionRegistry.Add(player.PlayerUID, index);
                /*
                //need to come back to this
                //serverChannel.SendPacket(new DimensionIndexResponse { index = index }, ((IServerPlayer)player));
                sapi.World.SpawnEntity(EntityVehicle.CreateVehicle(sapi, dim));
                */
            }
        }
        //Check if the player is in the minidimension registry
        //if not, create and load a minidimension, then add them to the registry
        
        //Mod.Logger.Notification("Player joined: " + player.PlayerUID);
    }

    private void OnSaveGameLoaded()
    {
        byte[] data = sapi.WorldManager.SaveGame.GetData("Vehicle.DimensionRegistry");
        _dimensionRegistry = data == null ? new Dictionary<string, int>() : SerializerUtil.Deserialize<Dictionary<string, int>>(data);
        //Load all minidimensions from the registry
        //Load all blocks from each minidimension into the world, to be added later
    }

    private void OnGameWorldSave()
    {
        
        //May need to define how _dimensionRegistry is serialized
        //check how schematics are saved and copy that? Could get big and messy
        //empty dimensions should get skipped, will add later
        sapi.WorldManager.SaveGame.StoreData("Vehicle.DimensionRegistry", SerializerUtil.Serialize(_dimensionRegistry));
    }
    
    public override void Dispose() 
    {
        //harmony.UnpatchAll(Mod.Info.ModID);
    }
}