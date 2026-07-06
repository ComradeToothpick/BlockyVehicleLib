using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using BlockyVehicleLib.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace BlockyVehicleLib.Blocks;

public class ModSystemBlockConstruction : ModSystem
{
    private ICoreAPI api;

    private IServerNetworkChannel serverChannel;
    
    private Dictionary<string, ReinforcedPrivilegeGrants> privGrantsByOwningPlayerUid = new Dictionary<string, ReinforcedPrivilegeGrants>();
    private Dictionary<int, ReinforcedPrivilegeGrantsGroup> privGrantsByOwningGroupUid = new Dictionary<int, ReinforcedPrivilegeGrantsGroup>();
    
    public bool reasonableConstructions = true;
    
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    public override void Start(ICoreAPI api)
    {
        this.api = api;
        //api.RegisterItemClass("ItemPlumbAndSquare", typeof(ItemPlumbAndSquare));
        api.RegisterBlockBehaviorClass("Constructable", typeof(BlockBehaviorConstructable));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side == EnumAppSide.Server)
        {
            this.addConstructableBehavior();
        }
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        api.Network.RegisterChannel("blockconstructable").RegisterMessageType(typeof (ChunkConstructionData))
            .RegisterMessageType(typeof(ChunkConstructionData))
            .RegisterMessageType(typeof (PrivGrantsData))
            .SetMessageHandler<ChunkConstructionData>(new NetworkServerMessageHandler<ChunkConstructionData>(this.onChunkData))
            .SetMessageHandler<PrivGrantsData>(new NetworkServerMessageHandler<PrivGrantsData>(this.onPrivData));

    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.SaveGameLoaded += this.Event_SaveGameLoaded;
        api.Event.GameWorldSave += this.Event_GameWorldSave;
        api.Event.PlayerJoin += this.Event_PlayerJoin;
        
        serverChannel = api.Network.RegisterChannel("blockconstructable")
            .RegisterMessageType(typeof (ChunkConstructionData))
            .RegisterMessageType(typeof (PrivGrantsData));
        /*CommandArgumentParsers parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("bvl").WithDescription("Player owned Block reinforcement privilege management").RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("grant")
            .WithDescription("Grant a player access to your block reinforcements")
            .WithArgs(new ICommandArgumentParser[]
            {
                parsers.Word("playername"),
                parsers.WordRange("flag", new string[] { "all", "use" })
            })
            .HandleWith(new OnCommandDelegate(this.OnCmdGrant))
            .EndSubCommand()
            .BeginSubCommand("revoke")
            .WithDescription("Revoke access for a player to your block reinforcements")
            .WithArgs(new ICommandArgumentParser[] { parsers.Word("playername") })
            .HandleWith(new OnCommandDelegate(this.OnCmdRevoke))
            .EndSubCommand()
            .BeginSubCommand("grantgroup")
            .WithDescription("Grant a group access to your block reinforcements")
            .WithArgs(new ICommandArgumentParser[]
            {
                parsers.Word("groupname"),
                parsers.WordRange("flag", new string[] { "all", "use" })
            })
            .HandleWith(new OnCommandDelegate(this.OnCmdGrantGroup))
            .EndSubCommand()
            .BeginSubCommand("revokegroup")
            .WithDescription("Revoke access for a group to your block reinforcements")
            .WithArgs(new ICommandArgumentParser[] { parsers.Word("groupname") })
            .HandleWith(new OnCommandDelegate(this.OnCmdRevokeGroup))
            .EndSubCommand();
        api.ChatCommands.Create("gbre").WithDescription("Group owned Block reinforcement privilege management").RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("grant")
            .WithDescription("Grant a player access to your groups block reinforcements. Use default as group name to change the access type for members")
            .WithArgs(new ICommandArgumentParser[]
            {
                parsers.Word("playername"),
                parsers.WordRange("flag", new string[] { "all", "use" })
            })
            .HandleWith(new OnCommandDelegate(this.OnCmdGroupGrant))
            .EndSubCommand()
            .BeginSubCommand("revoke")
            .WithDescription("Revoke a player access to your groups block reinforcements. Use default as group name to revoke the access type for goup members")
            .WithArgs(new ICommandArgumentParser[] { parsers.Word("playername") })
            .HandleWith(new OnCommandDelegate(this.OnCmdGroupRevoke))
            .EndSubCommand()
            .BeginSubCommand("grantgroup")
            .WithDescription("Grant an other group access to your groups block reinforcements")
            .WithArgs(new ICommandArgumentParser[]
            {
                parsers.Word("groupname"),
                parsers.WordRange("flag", new string[] { "all", "use" })
            })
            .HandleWith(new OnCommandDelegate(this.OnCmdGroupGrantGroup))
            .EndSubCommand()
            .BeginSubCommand("revokegroup")
            .WithDescription("Revoke an others groups access to your groups block reinforcements")
            .WithArgs(new ICommandArgumentParser[] { parsers.Word("groupname") })
            .HandleWith(new OnCommandDelegate(this.OnCmdGroupRevokeGroup))
            .EndSubCommand();
        api.Permissions.RegisterPrivilege("denybreakreinforced", "Deny the ability to break reinforced blocks", false);
        #2##1#*/
    }
    
    protected void onChunkData(ChunkConstructionData msg)
    {
        IWorldChunk chunk = this.api.World.BlockAccessor.GetChunk(msg.chunkX, msg.chunkY, msg.chunkZ);
        if (chunk != null)
        {
            chunk.SetModdata("constructions", msg.Data);
        }
    }
    
    protected static EnumBlockAccessFlags GetFlags(string flagString)
    {
        EnumBlockAccessFlags flags = EnumBlockAccessFlags.None;
        if (flagString != null)
        {
            if (flagString.ToLowerInvariant() == "use")
            {
                flags = EnumBlockAccessFlags.Use;
            }
            if (flagString.ToLowerInvariant() == "all")
            {
                flags = EnumBlockAccessFlags.BuildOrBreak | EnumBlockAccessFlags.Use;
            }
        }
        return flags;
    }
    
    protected void Event_PlayerJoin(IServerPlayer byPlayer)
    {
        IServerNetworkChannel serverNetworkChannel = this.serverChannel;
        if (serverNetworkChannel == null)
        {
            return;
        }
        serverNetworkChannel.SendPacket<PrivGrantsData>(new PrivGrantsData
        {
            privGrantsByOwningPlayerUid = this.privGrantsByOwningPlayerUid,
            privGrantsByOwningGroupUid = this.privGrantsByOwningGroupUid
        }, new IServerPlayer[] { byPlayer });
    }
    
    protected void Event_GameWorldSave()
    {
        (this.api as ICoreServerAPI).WorldManager.SaveGame.StoreData("blockconstructionprivileges", SerializerUtil.Serialize<Dictionary<string, ReinforcedPrivilegeGrants>>(this.privGrantsByOwningPlayerUid));
        (this.api as ICoreServerAPI).WorldManager.SaveGame.StoreData("blockconstructionprivilegesgroup", SerializerUtil.Serialize<Dictionary<int, ReinforcedPrivilegeGrantsGroup>>(this.privGrantsByOwningGroupUid));
    }

    protected void Event_SaveGameLoaded()
    {
        byte[] data1 = (this.api as ICoreServerAPI).WorldManager.SaveGame.GetData("blockconstructionprivileges");
        if (data1 != null)
        {
            try
            {
                this.privGrantsByOwningPlayerUid = SerializerUtil.Deserialize<Dictionary<string, ReinforcedPrivilegeGrants>>(data1);
            }
            catch
            {
                this.api.World.Logger.Notification("Unable to load player->group privileges for the block construction system. Exception thrown when trying to deserialize it. Will be discarded.");
            }
        }
        byte[] data2 = (this.api as ICoreServerAPI).WorldManager.SaveGame.GetData("blockconstructionprivilegesgroup");
        if (data2 == null)
            return;
        try
        {
            this.privGrantsByOwningGroupUid = SerializerUtil.Deserialize<Dictionary<int, ReinforcedPrivilegeGrantsGroup>>(data2);
        }
        catch
        {
            this.api.World.Logger.Notification("Unable to load group->player privileges for the block construction system. Exception thrown when trying to deserialize it. Will be discarded.");
        }
    }
    
    protected void addConstructableBehavior()
    {
        foreach (Block block in this.api.World.Blocks)
        {
            if (!(block.Code == null) && block.Id != 0 && this.IsConstructable(block))
            {
                block.BlockBehaviors = block.BlockBehaviors.Append(new BlockBehaviorConstructable(block));
                block.CollectibleBehaviors = block.CollectibleBehaviors.Append(new BlockBehaviorConstructable(block));
            }
        }
    }
    
    protected bool IsConstructable(Block block)
    {//Leave this checking for the reinforcable trait because it saves having to sort that out myself
        return (!this.reasonableConstructions || (block.BlockMaterial != EnumBlockMaterial.Plant && block.BlockMaterial != EnumBlockMaterial.Water && block.BlockMaterial != EnumBlockMaterial.Snow && block.BlockMaterial != EnumBlockMaterial.Leaves && block.BlockMaterial != EnumBlockMaterial.Lava && block.BlockMaterial != EnumBlockMaterial.Sand && block.BlockMaterial != EnumBlockMaterial.Gravel) || (block.Attributes != null && block.Attributes["reinforcable"].AsBool(false))) && (block.Attributes == null || block.Attributes["reinforcable"].AsBool(true));
    }
    
    public ItemSlot FindResourceForConstructing(IPlayer byPlayer)
    {
        ItemSlot foundSlot = null;
        byPlayer.Entity.WalkInventory(delegate(ItemSlot onSlot)
        {
            if (onSlot.Itemstack == null || onSlot.Itemstack.ItemAttributes == null)
            {
                return true;
            }
            if (onSlot is ItemSlotCreative)
            {
                return true;
            }
            if (!(onSlot.Inventory is InventoryBasePlayer))
            {
                return true;
            }
            int? num = new int?(onSlot.Itemstack.ItemAttributes["constructionStrength"].AsInt(0));
            int num2 = 0;
            if ((num.GetValueOrDefault() > num2) & (num != null))
            {
                foundSlot = onSlot;
                return false;
            }
            return true;
        });
        return foundSlot;
    }

    public BlockConstruction GetConstruction(BlockPos pos)
    {
        Dictionary<int, BlockConstruction> constructionsAt = this.getOrCreateConstructionsAt(pos);
        if (constructionsAt == null)
            return (BlockConstruction) null;
        int localIndex = this.toLocalIndex(pos);
        return !constructionsAt.ContainsKey(localIndex) ? (BlockConstruction) null : constructionsAt[localIndex];
    }
    
    protected Dictionary<int, BlockConstruction> getOrCreateConstructionsAt(BlockPos pos)
    {
        IWorldChunk chunkAtBlockPos = this.api.World.BlockAccessor.GetChunkAtBlockPos(pos);
        if (chunkAtBlockPos == null)
            return (Dictionary<int, BlockConstruction>) null;
        byte[] moddata = chunkAtBlockPos.GetModdata("constructions");
        Dictionary<int, BlockConstruction> constructionsAt;
        if (moddata != null)
        {
            try
            {
                constructionsAt = SerializerUtil.Deserialize<Dictionary<int, BlockConstruction>>(moddata);
            }
            catch (Exception ex1)
            {
                this.api.World.Logger.VerboseDebug("Failed reading block constructions at block position {0}, will discard, sorry.", (object) pos);
                this.api.World.Logger.VerboseDebug(LoggerBase.CleanStackTrace(ex1.ToString()));
                constructionsAt = new Dictionary<int, BlockConstruction>();
            }
        }
        else
            constructionsAt = new Dictionary<int, BlockConstruction>();
        return constructionsAt;
    }
    
    protected void SaveConstructions(Dictionary<int, BlockConstruction> reif, BlockPos pos)
    {
        int chunkX = pos.X / 32 /*0x20*/;
        int chunkY = pos.Y / 32 /*0x20*/;
        int chunkZ = pos.Z / 32 /*0x20*/;
        byte[] data = SerializerUtil.Serialize<Dictionary<int, BlockConstruction>>(reif);
        this.api.World.BlockAccessor.GetChunk(chunkX, chunkY, chunkZ).SetModdata("constructions", data);
        IServerNetworkChannel serverChannel = this.serverChannel;
        if (serverChannel == null)
            return;
        serverChannel.BroadcastPacket<ChunkConstructionData>(new ChunkConstructionData()
        {
            chunkX = chunkX,
            chunkY = chunkY,
            chunkZ = chunkZ,
            Data = data
        });
    }

    public void ClearConstruction(BlockPos pos)
    {
        Dictionary<int, BlockConstruction> constructionsAt = this.getOrCreateConstructionsAt(pos);
        if (constructionsAt == null)
            return;
        int localIndex = this.toLocalIndex(pos);
        if (!constructionsAt.ContainsKey(localIndex) || !constructionsAt.Remove(localIndex))
            return;
        this.SaveConstructions(constructionsAt, pos);
    }
    
    public bool IsConstructed(BlockPos pos)
    {
        Dictionary<int, BlockConstruction> constructionsOfChunk = this.getOrCreateConstructionsAt(pos);
        if (constructionsOfChunk == null)
        {
            return false;
        }
        int index3d = this.toLocalIndex(pos);
        return constructionsOfChunk.ContainsKey(index3d);
    }
    
    public bool IsLockedForInteract(BlockPos pos, IPlayer forPlayer)
    {
        Dictionary<int, BlockConstruction> constructionsOfChunk = this.getOrCreateConstructionsAt(pos);
        if (constructionsOfChunk == null)
        {
            return false;
        }
        int index3d = this.toLocalIndex(pos);
        BlockConstruction bco;
        return constructionsOfChunk.TryGetValue(index3d, out bco) && bco.Locked && bco.PlayerUID != forPlayer.PlayerUID && forPlayer.GetGroup(bco.GroupUid) == null && (this.GetAccessFlags(bco.PlayerUID, bco.GroupUid, forPlayer) & EnumBlockAccessFlags.Use) <= EnumBlockAccessFlags.None && (forPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative || !forPlayer.HasPrivilege(Privilege.commandplayer));
    }
    
    public EnumBlockAccessFlags GetAccessFlags(
        string owningPlayerUid,
        int owningGroupId,
        IPlayer forPlayer)
    {
        if (owningPlayerUid == forPlayer.PlayerUID)
            return EnumBlockAccessFlags.BuildOrBreak | EnumBlockAccessFlags.Use;
        PlayerGroupMembership group = forPlayer.GetGroup(owningGroupId);
        if (group != null)
            return EnumBlockAccessFlags.BuildOrBreak | EnumBlockAccessFlags.Use;
        EnumBlockAccessFlags accessFlags = EnumBlockAccessFlags.None;
        ReinforcedPrivilegeGrants constructedPrivilegeGrants;
        if (owningPlayerUid != null && this.privGrantsByOwningPlayerUid.TryGetValue(owningPlayerUid, out constructedPrivilegeGrants))
        {
            constructedPrivilegeGrants.PlayerGrants.TryGetValue(forPlayer.PlayerUID, out accessFlags);
            foreach (KeyValuePair<int, EnumBlockAccessFlags> groupGrant in constructedPrivilegeGrants.GroupGrants)
            {
                if (forPlayer.GetGroup(groupGrant.Key) != null)
                    accessFlags |= groupGrant.Value;
            }
        }
        ReinforcedPrivilegeGrantsGroup privilegeGrantsGroup;
        if (owningGroupId != 0 && this.privGrantsByOwningGroupUid.TryGetValue(owningGroupId, out privilegeGrantsGroup))
        {
            if (group != null)
            {
                privilegeGrantsGroup.PlayerGrants.TryGetValue(forPlayer.PlayerUID, out accessFlags);
                accessFlags |= privilegeGrantsGroup.DefaultGrants;
            }
            foreach (KeyValuePair<int, EnumBlockAccessFlags> groupGrant in privilegeGrantsGroup.GroupGrants)
            {
                if (forPlayer.GetGroup(groupGrant.Key) != null)
                    accessFlags |= groupGrant.Value;
            }
        }
        return accessFlags;
    }
    
    protected int toLocalIndex(BlockPos pos)
    {
        return this.toLocalIndex(pos.X % 32, pos.Y % 32, pos.Z % 32);
    }
    
    protected int toLocalIndex(int x, int y, int z)
    {
        return (y << 16) | (z << 8) | x;
    }

    public void ConsumeStrength(BlockPos pos, int byAmount)
    {
        Dictionary<int, BlockConstruction> constructionsAt = this.getOrCreateConstructionsAt(pos);
        if (constructionsAt == null)
            return;
        int localIndex = this.toLocalIndex(pos);
        if (!constructionsAt.ContainsKey(localIndex))
            return;
        constructionsAt[localIndex].Strength -= byAmount;
        if (constructionsAt[localIndex].Strength <= 0)
            constructionsAt.Remove(localIndex);
        this.SaveConstructions(constructionsAt, pos);
    }
    
    public bool StrengthenBlock(BlockPos pos, IPlayer byPlayer, int strength, int forGroupUid = 0)
    {
        if (this.api.Side == EnumAppSide.Client)
        {
            return false;
        }
        api.Logger.Event("StrengthenBlock");
        if (!this.api.World.BlockAccessor.GetBlock(pos, 1).HasBehavior<BlockBehaviorConstructable>(false))
        {
            api.Logger.Event("Block doesn't have constructable behaviour");
            return false;
        }
        Dictionary<int, BlockConstruction> constructionsAt = this.getOrCreateConstructionsAt(pos);
        int localIndex = this.toLocalIndex(pos);
        if (constructionsAt.ContainsKey(localIndex))
        {
            BlockConstruction blockConstruction = constructionsAt[localIndex];
            if (blockConstruction.Strength > 0)
            {
                api.Logger.Event("Block already constructed, strength: " + blockConstruction.Strength);
                return false;
            }
            blockConstruction.Strength = strength;
            api.Logger.Event("Block constructed, strength: " + blockConstruction.Strength);
        }
        else
        {
            api.Logger.Event("Creating new construction at localIndex: " + localIndex);
            string str = (string) null;
            PlayerGroup playerGroup;
            if ((this.api as ICoreServerAPI).Groups.PlayerGroupsById.TryGetValue(forGroupUid, out playerGroup))
                str = playerGroup.Name;
            constructionsAt[localIndex] = new BlockConstruction()
            {
                PlayerUID = forGroupUid == 0 ? byPlayer.PlayerUID : (string) null,
                GroupUid = forGroupUid,
                LastPlayername = byPlayer.PlayerName,
                LastGroupname = str,
                Strength = strength
            };
        }
        this.SaveConstructions(constructionsAt, pos);
        return true;
    }
    
    public void StrengthenMultiBlocks(BlockPos startPos, BlockPos endPos, IPlayer byPlayer, int strength, int forGroupUid = 0)
    {
        if (this.api.Side == EnumAppSide.Client)
        {
            return;
        }

        Dictionary<int, BlockConstruction> constructionsAt;
        api.Logger.Event("StrengthenBlock");
        api.World.BlockAccessor.WalkBlocks(startPos, endPos, (block, x, y, z) =>
        {
            BlockPos pos = new BlockPos(x, y, z);
            if (block.HasBehavior<BlockBehaviorConstructable>(false))
            {
                constructionsAt = this.getOrCreateConstructionsAt(pos);
                int localIndex = this.toLocalIndex(pos);
                if (constructionsAt.ContainsKey(localIndex))
                {
                    BlockConstruction blockConstruction = constructionsAt[localIndex];
                    if (!(blockConstruction.Strength > 0))
                    {
                        blockConstruction.Strength = strength;
                    }
                }
                else
                {
                    string str = (string)null;
                    PlayerGroup playerGroup;
                    if ((this.api as ICoreServerAPI).Groups.PlayerGroupsById.TryGetValue(forGroupUid, out playerGroup))
                        str = playerGroup.Name;
                    constructionsAt[localIndex] = new BlockConstruction()
                    {
                        PlayerUID = forGroupUid == 0 ? byPlayer.PlayerUID : (string)null,
                        GroupUid = forGroupUid,
                        LastPlayername = byPlayer.PlayerName,
                        LastGroupname = str,
                        Strength = strength
                    };
                }
                this.SaveConstructions(constructionsAt, pos);
            }
        });
        
        
    }
    
    protected void onPrivData(PrivGrantsData networkMessage)
    {
        this.privGrantsByOwningPlayerUid = networkMessage.privGrantsByOwningPlayerUid;
        this.privGrantsByOwningGroupUid = networkMessage.privGrantsByOwningGroupUid;
    }

    protected void SyncPrivData()
    {
        IServerNetworkChannel serverChannel = this.serverChannel;
        if (serverChannel == null)
            return;
        serverChannel.BroadcastPacket<PrivGrantsData>(new PrivGrantsData()
        {
            privGrantsByOwningPlayerUid = this.privGrantsByOwningPlayerUid,
            privGrantsByOwningGroupUid = this.privGrantsByOwningGroupUid
        });
    }
}