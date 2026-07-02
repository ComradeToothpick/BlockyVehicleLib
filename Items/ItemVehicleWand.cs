using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Vintagestory;
using BlockyVehicleLib.Entities;
using BlockyVehicleLib.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

#nullable disable

namespace BlockyVehicleLib.Items;
//What does this need to do?
//It needs to select a volume or a group of tethered together (by some as yet undescribed means (reinforced with nails? or just yoink the code from that)) blocks
//It needs to call a function using the above as an input to create the entity and put the blocks into a shipyard (pocket dimension or beyond the world border)
//It needs to provide information about the entity or blocks
//It needs to be able to turn Vehicle entities back into blocks by calling the requisite function
//most of the code for the functionality of this item will probably sit in other files
public class ItemVehicleWand : Item
{
    //private double spawningTime = 1.0;
    //public override void OnLoaded(ICoreAPI api)
    //{
    //    ((CollectibleObject) this).OnLoaded(api);
    //}

    //private ICoreAPI api;
    
    private IClientNetworkChannel clientChannel;
    private IServerNetworkChannel serverChannel;
    public int DimensionIndex = -1;
    private EntityChunky entity = null;
    private Vec3d pos;
    private EntityPlayer playerEntity;
    private IPlayer player;
    //limit use of modSystem, always check if the api is client or server before using it
    private BlockyVehicleLibModSystem modSystem;

    public override void OnLoaded(ICoreAPI coreApi)
    {
        api = coreApi;
        if (api is ICoreClientAPI)
        {
            clientChannel = ((ICoreClientAPI)api).Network.GetChannel("VehicleNetworkApi")
                .RegisterMessageType<DimensionIndexRequest>()
                .RegisterMessageType<DimensionSpawnRequest>();
        }
        if (api is ICoreServerAPI)
        {
            modSystem = ((ICoreServerAPI)api).ModLoader.GetModSystem<BlockyVehicleLibModSystem>();
            serverChannel = ((ICoreServerAPI)api).Network.GetChannel("VehicleNetworkApi")
                .RegisterMessageType<DimensionIndexRequest>()
                .RegisterMessageType<DimensionSpawnRequest>()
                .SetMessageHandler<DimensionIndexRequest>(modSystem.OnDimensionIndexRequest)
                .SetMessageHandler<DimensionSpawnRequest>(modSystem.OnDimensionSpawnRequest);
        }
        
        base.OnLoaded(coreApi);
    }
    
    /*
    public void OnDimensionIndexResponse(DimensionIndexResponse message)
    {
        api.Logger.Event("ItemVehicleWand.OnDimensionIndexResponse: " + message.index);
        DimensionIndex = message.index;
        IMiniDimension dim = ((ICoreClientAPI)api).World.GetOrCreateDimension(DimensionIndex, pos);
        api.Logger.Event("attempting to create an EntityChunky");
        entity = EntityVehicle.CreateVehicle((ICoreClientAPI)api, dim);
                
        playerEntity.World.SpawnEntity(entity);
        
        _isSpawning = false;
        api.Logger.Event("entity spawned");
    }
    */
    public override void OnHeldInteractStart(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        EntitySelection entitySel,
        bool firstEvent,
        ref EnumHandHandling handHandling)
    {
        playerEntity = (EntityPlayer) byEntity;
        player = playerEntity.World.PlayerByUid(playerEntity.PlayerUID);
        api.Logger.Event("OnHeldInteractStart started");
        //((CollectibleObject) this).OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
        if (handHandling == EnumHandHandling.PreventDefault) 
            return;
        if (blockSel == null)
        {
            api.Logger.Event("blockSel == null");
            return;
        }
        //ProcessVehicleSpawningStart(byEntity, blockSel, ref handHandling);
        
        
        if (!(playerEntity).World.Claims.TryAccess(player, blockSel.Position, (EnumBlockAccessFlags) 1))
            return;
        IBlockAccessor blockAccessor = playerEntity.World.BlockAccessor;
        
        /*
        if (!(block.Code.Equals((AssetLocation) null)))
        {
            string lowerInvariant1 = ((RegistryObject) block).Code.Path.ToLowerInvariant();
            api.Logger.Event(lowerInvariant1);
        }*/
        
        AssetLocation assetLocation = new AssetLocation(Code.Domain, CodeEndWithoutParts(1));
        api.Logger.Event("Code.Domain: " + Code.Domain);
        api.Logger.Event("CodeEndWithoutParts: " + CodeEndWithoutParts(1));
        
        EntityProperties entityType = byEntity.World.GetEntityType(assetLocation);
        if (entityType == null)
        {
            api.Logger.Event("entityType == null");
            ((Entity)byEntity).World.Logger.Error(
                "ItemVehicleWand: No such entity - vehicle");
        }
        else
        {
            pos = new Vec3d(
                (double) (blockSel.Position.X + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.X)) + 0.5,
                (double) (blockSel.Position.Y + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Y)),
                (double) (blockSel.Position.Z + (blockSel.DidOffset ? 0 : blockSel.Face.Normali.Z)) + 0.5);
            api.Logger.Event("attempting to create a mini dimension");
            
            ProcessVehicleSpawnStart(blockSel);
            //entity.Pos.Yaw = ((Vintagestory.API.Common.Entities.Entity) byEntity).Pos.Yaw + 3.1415927f;
            //entity.Pos.Dimension = blockSel.Position.dimension;
            //entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            //((TreeAttribute) entity.Attributes).SetString("origin", "playerplaced");
            //JsonObject attributes = ((CollectibleObject) this).Attributes;
            //((TreeAttribute) entity.WatchedAttributes).SetBool("noSpawnAnim", true);
            
            handHandling = EnumHandHandling.PreventDefault;
            return;
        }
    }

    public async void ProcessVehicleSpawnStart(BlockSelection blockSel)
    {
        if (this.api is ICoreClientAPI)
        {
            
            //It's ok to not reset the dimension index here, because it's associated with the player.
            //Reset will need to happen if the item is ever removed from the player's inventory.
            if (DimensionIndex == -1)
            {
                clientChannel.SendPacket(new DimensionIndexRequest() { playerName = player.PlayerUID, vehicleWandID = this.Id});
                await Waiting();
                if (DimensionIndex == -1)
                {
                    api.Logger.Event("Operation time out: DimensionIndex == -1");
                    return;
                }
            }

            
            clientChannel.SendPacket(new DimensionSpawnRequest() { dimensionIndex = DimensionIndex, pos = pos, blockSel = blockSel, blockId = blockSel.Block.BlockId});
        }
    }

    private async Task Waiting()
    {
        int i = 0;
        while (DimensionIndex == -1 && i < 1000) {
            i++;
            await Task.Delay(1);
        }
        api.Logger.Event("ItemVehicleWand.DimensionIndex == " + DimensionIndex);
    }
}