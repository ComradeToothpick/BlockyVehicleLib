using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using BlockyVehicleLib.Blocks;
using BlockyVehicleLib.Items;
using BlockyVehicleLib.Entities;
using BlockyVehicleLib.Network;
using Newtonsoft.Json.Linq;
using PhysicsLib;
using PhysicsLib.Api;
using PhysicsLib.Entities.Behaviours;
using PhysicsLib.patches;
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
        harmony = new Harmony(Mod.Info.ModID);
        harmony.PatchAll();
        
        api.Logger.Event(Mod.Info.ModID + ".vehicle");
        api.RegisterEntity(Mod.Info.ModID + ".vehicle", typeof(EntityVehicle));
        api.RegisterItemClass(Mod.Info.ModID + ".vehiclewand", typeof(ItemVehicleWand));
        api.RegisterEntityBehaviorClass(Mod.Info.ModID + ".vehiclephysics", typeof(BehaviorPassivePhysicsVehicle));
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
        /*
        var BVLbehaviors = new List<JsonObject>(1);

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
        */
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
            .SetMessageHandler<VehicleEntityId>(OnVehicleEntityId)
            .SetMessageHandler<VehicleBlocks>(BuildVehicleColliders);
        //Mod.Logger.Notification("Hello from template mod client side: " + Lang.Get("Vehicle:hello"));
    }

    private void BuildVehicleColliders(VehicleBlocks message)
    {
        int[] blockIds = message.blockIds;
        BlockPos[] localPos = message.localPos;
        int dimId = message.dimId;
        long entityId = message.entityId;
        Vec3d CoM = message.CoM;
        CompoundCollider? cachedShapes = CollectBlocks(blockIds, localPos, dimId, entityId, CoM);
        if (cachedShapes == null)
        {
            api.Logger.Event("Vehicle collider construction failed!");
        }
    }

    private void OnVehicleEntityId(VehicleEntityId message)
    {
        //send the EntityId to the client.player.playerentity.EntityBehaviourVehiclePhysics.collisionTester
        //capi.World.Player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>().AddVehicle(message.entityId, message.subDimensionId);
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
        
        //The construction mode must be collected on the server side from the server side version of the wand, as the client side cannot correctly choose the right mode
        IMiniDimension? messageDim = sapi.Server.GetMiniDimension(message.dimensionIndex);
        BlockyVehicle dim;
        bool loadedDim = false;
        //set the loaded minidimension to the correct index (should be unnecessary in current state, but will keep for future proofing)
        BlockPos pos = message.blockSel.Position.Copy();
        /*Block b = message.blockSel.Block;
        if (b is BlockMicroBlock)
        {
            Cuboidf[] microColliders = ((BlockMicroBlock)b).GetCollisionBoxes(api.World.BlockAccessor, pos);
        }*/
        IPlayer[] players = sapi.Server.Players;
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
        BlockPos dimPos = new BlockPos(new Vec3i(0, 0, 0), 1);
        //pos.Sub(message.blockSel.Position);
        //pos.SetDimension(1);
        dim.AdjustPosForSubDimension(ref dimPos);
        BlockPos localOrigin = dimPos.CopyAndCorrectDimension();
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
        entity.Pos.SetPos(pos.X + 0.5f, pos.Y, pos.Z + 0.5f);
        entity.BlocksInitialize();
        dim.CurrentPos.SetPos(entity.Pos);
        if (loadedDim) EntityVehicle.InitializeVehicle(entity, dim);//re-initialize to fix desync issue
        serverChannel.SendPacket(new DimensionSpawnClientResponse() {dimId = dim.subDimensionId, blockPos = pos, vecPos = message.pos, blockId = message.blockId}, (IServerPlayer) player);
        await WaitingOnClient();
        //Do these after client side
        int blockId = message.blockId;
        int[] blockIds;
        BlockPos[] localPos;
        if (message.mode == EnumVehicleMode.Debug)
        {
            DebugBuild(blockId, dim, dimPos, localOrigin, out blockIds, out localPos);
        }
        else if (message.mode == EnumVehicleMode.Construction)
        {
            if(!ConstructionBuild(pos, dim, localOrigin, player, out blockIds, out localPos)) api.Logger.Error("[BlockyVehicleModSystem.OnDimensionSpawnRequest] Failed to build blocks!");;
        }
        else
        {
            DebugBuild(blockId, dim, dimPos, localOrigin, out blockIds, out localPos);
        }
        
        entity.BlocksDirty();
        //Send relevant information to the client side to build the colliders
        dim.RecalculateCenterOfMass(api.World);
        serverChannel.SendPacket(new VehicleBlocks() {blockIds =  blockIds, localPos = localPos, dimId = dim.subDimensionId, entityId = entity.EntityId, CoM = dim.CenterOfMass}, (IServerPlayer) player);
        api.Logger.Event("Block ID: " + dim.GetBlockId(dimPos));
        //sapi.World.BlockAccessor.SetBlock(blockId, pos2, 0);
        //dim.UnloadUnusedServerChunks();
        
        dim.CollectChunksForSending(players);
        api.Logger.Event("Vehicle Spawned Successfully");
    }

    public void DebugBuild(int blockId, BlockyVehicle dim, BlockPos dimPos, BlockPos localOrigin, out int[] blockIds, out BlockPos[] localPos)
    {
        blockIds = new int[20];
        localPos = new BlockPos[20];
        //int maxX = 0; int maxY = 0; int maxZ = 0;
        for (int i = 0; i < 10; i++)//Staircase formation
        {//Need to swap X and Z components of localPos, not totally sure why
            dim.SetBlock(blockId, dimPos, new ItemStack());
            blockIds[2 * i] = dim.GetBlock(dimPos).BlockId;
            localPos[2 * i] = new BlockPos(dimPos.Z - localOrigin.Z,  dimPos.Y - localOrigin.Y, dimPos.X - localOrigin.X);
            dimPos.X += 1;
            dim.SetBlock(blockId, dimPos, new ItemStack());
            blockIds[2 * i + 1] = dim.GetBlock(dimPos).BlockId;
            localPos[2 * i + 1] = new BlockPos(dimPos.Z - localOrigin.Z,  dimPos.Y - localOrigin.Y, dimPos.X - localOrigin.X);
            dimPos.Y += 1;
        }
    }

    public bool ConstructionBuild(BlockPos startPos, BlockyVehicle dim, BlockPos localOrigin, IPlayer player, out int[] blockIds,
        out BlockPos[] localPos)
    {
        Dictionary<BlockPos, int> cBlocks = GetConstructedBlocks(startPos);
        if (cBlocks.Count == 0 || cBlocks == null)
        {
            api.Logger.Error("[BlockyVehicleModSystem.ConstructionBuild] No Blocks Found");
            localPos = new BlockPos[1];
            blockIds = new int[1];
            return false;
        }
        localPos = (BlockPos[])cBlocks.Keys.ToArray().Clone();
        blockIds = (int[])cBlocks.Values.ToArray().Clone();
        ModSystemBlockConstruction bco = api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        for (int i = 0; i < cBlocks.Count; i++)
        {
            BlockPos newPos = localPos[i] + localOrigin;
            dim.SetBlock(blockIds[i], newPos, new ItemStack());
        }
        
        foreach (BlockPos blockPos in cBlocks.Keys)
        {
            bco.ClearConstruction(blockPos + startPos);
            api.World.BlockAccessor.BreakBlock(blockPos + startPos, player, 0F);
        }
        return true;
    }

    private Dictionary<BlockPos, int> GetConstructedBlocks(BlockPos startPos)//This is a rather inefficient method, plenty of room to optimise
    {
        List<BlockPos> stack = new List<BlockPos>();
        List<BlockPos> done = new List<BlockPos>();
        Dictionary<BlockPos, int> dimBlocks = new Dictionary<BlockPos, int>();
        BlockPos pos = startPos;
        stack.Add(pos);
        IBlockAccessor blockAccessor = api.World.BlockAccessor;
        ModSystemBlockConstruction mod = api.ModLoader.GetModSystem<ModSystemBlockConstruction>();
        bool searching = true;
        while (searching)
        {
            pos = stack[^1];
            done.Add(pos);
            stack.RemoveAt(stack.Count - 1);
            
            if (mod.IsConstructed(pos))
            {//Need to swap X and Z components of deltaPos, not totally sure why
                BlockPos deltaPos = new BlockPos(pos.Z - startPos.Z, pos.Y - startPos.Y, pos.X - startPos.X, 0) ;
                int blockId = blockAccessor.GetBlock(pos).BlockId;
                if (!dimBlocks.TryAdd(deltaPos, blockId)) api.Logger.Event("[BlockyVehicleModSystem.GetConstructedBlocks] Failed to collect Pos: " + pos + ", Block ID: " + blockId);
                BlockPos blockPos = pos.DownCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
                blockPos = pos.UpCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
                blockPos = pos.NorthCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
                blockPos = pos.SouthCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
                blockPos = pos.EastCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
                blockPos = pos.WestCopy();
                if (!(stack.Contains(blockPos) || done.Contains(blockPos))) stack.Add(blockPos);
            }
            if (stack.Count == 0) searching = false;
        }
        
        return dimBlocks;
    }

    public CompoundCollider? CollectBlocks(int[] blockIds, BlockPos[] localPos, int dimId, long entityId, Vec3d CoM)
    {
        BlockyVehicle dim;
        if (api.Side == EnumAppSide.Client)
        {
            dim = (BlockyVehicle)((ICoreClientAPI)api).World.MiniDimensions[dimId];
        }
        else
        {
            dim = _loadedMinidimensions[dimId];
        }
        Vec3f[] vecLocalPos = new Vec3f[localPos.Length];
        for (int i = 0; i < localPos.Length; i++)
        {
            vecLocalPos[i] = new Vec3f();//Swapping x and z to correct orientation
            vecLocalPos[i].X = (float) (localPos[i].X - CoM.X);
            vecLocalPos[i].Y = (float) (localPos[i].Y - CoM.Y);
            vecLocalPos[i].Z = (float) (localPos[i].Z - CoM.Z);
        }
        Entity entity = api.World.GetEntityById(entityId);
        if (entity == null)
        {
            api.Logger.Event("[BlockyVehicleModSystem.CollectBlocks] Entity Not Found");
            return null;
        }
        DynamicPhysicsBehaviour? behaviour = entity.GetBehavior<DynamicPhysicsBehaviour>();
        if (behaviour == null)
        {
            api.Logger.Event("[BlockyVehicleModSystem.CollectBlocks] Dynamic Physics Behaviour Not Found");
            return null;
        }
        List<LocalBox> boxList = new List<LocalBox>();
        api.Logger.Event("blockIds.Length: " + blockIds.Length);
        for (int i = 0; i < blockIds.Length; i++)
        {
            Block block = dim.GetBlock(blockIds[i]);
            int degY = 0;
            if (block.Code.Path.Contains("-north-"))
            {
                api.Logger.Event("Block North: " + block.Code.Path);
                degY = 90;
            }
            if (block.Code.Path.Contains("-west-"))
            {
                api.Logger.Event("Block West: " + block.Code.Path);
                degY = 180;
            }

            if (block.Code.Path.Contains("-south-"))
            {
                api.Logger.Event("Block South: " + block.Code.Path);
                degY = 270;
            }

            if (block.Code.Path.Contains("-east-"))
            {
                api.Logger.Event("Block East: " + block.Code.Path);
                degY = 0;
            }
            Cuboidf[] colliders = block.CollisionBoxes;
            foreach (Cuboidf collider in colliders)
            {
                Cuboidf collider2 = collider.RotatedCopy(0, degY, 0, new Vec3d(0.5, 0.5, 0.5));//will likely need to make this more sophisticated later
                //api.Logger.Event("Collision Box: " + collider.ToString());
                //api.Logger.Event("Collision Box Half Extents: [{0}, {1}, {2}]", (collider.X2 - collider.X1)/2, (collider.Y2-collider.Y1)/2 , (collider.Z2-collider.Z1)/2);
                //api.Logger.Event("Collision Box Center: [{0}, {1}, {2}]", (collider.X2 + collider.X1)/2, (collider.Y2 + collider.Y1)/2 , (collider.Z2 + collider.Z1)/2);
                collider2.X1 = (float)Math.Round(collider2.X1, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Y1 = (float)Math.Round(collider2.Y1, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Z1 = (float)Math.Round(collider2.Z1, 2, MidpointRounding.ToPositiveInfinity);
                collider2.X2 = (float)Math.Round(collider2.X2, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Y2 = (float)Math.Round(collider2.Y2, 2, MidpointRounding.ToPositiveInfinity);
                collider2.Z2 = (float)Math.Round(collider2.Z2, 2, MidpointRounding.ToPositiveInfinity);
                //api.Logger.Event("Collision Box2: [{0}, {1}, {2}] => [{3}, {4}, {5}]", collider2.X1, collider2.Y1, collider2.Z1,  collider2.X2, collider2.Y2, collider2.Z2);
                //api.Logger.Event("Collision Box2 Half Extents: [{0}, {1}, {2}]", (collider2.X2 - collider2.X1)/2, (collider2.Y2 - collider2.Y1)/2 , (collider2.Z2 - collider2.Z1)/2);
                //api.Logger.Event("Collision Box2 Center: [{0}, {1}, {2}]", (collider2.X2 + collider2.X1)/2, (collider2.Y2 + collider2.Y1)/2 , (collider2.Z2 + collider2.Z1)/2);
                LocalBox box = new LocalBox()
                {
                    LocalPosition = new Vector3(((collider2.X2 + collider2.X1)/2 + vecLocalPos[i].X), (collider2.Y2 + collider2.Y1)/2 + vecLocalPos[i].Y, (collider2.Z2 + collider2.Z1)/2 + vecLocalPos[i].Z),
                    LocalOrientation = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), (float)((degY) * Math.PI / 180)),
                    HalfExtents = new Vector3((collider2.X2-collider2.X1)/2, (collider2.Y2-collider2.Y1)/2, (collider2.Z2-collider2.Z1)/2)
                };
                boxList.Add(box);
            }
        }
        CompoundCollider cachedShapes = new CompoundCollider()
        {
            LocalCenterOfMassOffset = new Vector3((float)CoM.X, (float)CoM.Y, (float)CoM.Z),
            Boxes = boxList.ToArray()
        };
        PhysicsLibModSystem mod = api.ModLoader.GetModSystem<PhysicsLibModSystem>();
        behaviour.collider = cachedShapes;
        behaviour.boundingRadius = DynamicPhysicsBehaviour.ComputeBoundingRadius(cachedShapes);
        mod.AddCompound(entity.EntityId.ToString(), cachedShapes);
        mod.Registry.Register(behaviour);
        IsCollidingPatch.Registry.Register(behaviour);
        ApplyTerrainCollisionPatch.Registry.Register(behaviour);
        return cachedShapes;
    }

    private void OnDimensionSpawnClientResponse(DimensionSpawnClientResponse message)
    {
        if (api.Side != EnumAppSide.Client) return;
        BlockyVehicle dim = new BlockyVehicle((BlockAccessorBase)capi.World.BlockAccessor, message.vecPos, capi);
        dim.SetSubDimensionId(message.dimId);
        capi.World.MiniDimensions[message.dimId] = dim;
        //IMiniDimension dim = capi.World.GetOrCreateDimension(message.dimId, message.vecPos);
        
        Vec3d newPos = message.vecPos.Add(new Vec3f(0.5f, 1.0f, 0.5f));
        dim.CurrentPos.SetPos(newPos);
        dim.selectionTrackingOriginalPos = message.blockPos;//Need to set this for it to render in the world
        dim.selectionTrackingOriginalPos.Y += 1;
        capi.World.SpawnEntity(EntityVehicle.CreateVehicle(capi, dim));
        capi.Network.GetChannel("VehicleNetworkApi").SendPacket<DimensionSpawnClientComplete>(new DimensionSpawnClientComplete() {success = true});
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
            //player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>().entity = (Entity) player.Entity;
            //if (player.Entity.GetBehavior<EntityBehaviorVehiclePhysics>() == null) sapi.Logger.Event("Behavior not found");
            //else sapi.Logger.Event("Behavior found");
            //This testing revealed the behavior is getting added successfully.
            //So why is it doing nothing?
            int dimIndex = GetMiniDimensionPlayerIndex(player);
            if (dimIndex == -1)
            {
                BlockAccessorMovable dim = (BlockAccessorMovable) sapi.World.BlockAccessor.CreateMiniDimension(new Vec3d(0, 0, 0));
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
        harmony.UnpatchAll(Mod.Info.ModID);
    }
}