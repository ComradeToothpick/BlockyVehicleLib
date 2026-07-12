using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using BlockyVehicleLib.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using PhysicsLib.Api;
using PhysicsLib.Entities.Behaviours;
using Vintagestory.GameContent;
using Vintagestory.Server;
using static Vintagestory.API.Config.GlobalConstants;

namespace BlockyVehicleLib.Entities;

public class EntityVehicle : EntityChunky
{
    public int tickOffset = 0;
    protected int tickCounter = 0;
    public double[] qRotation;
    public double[] angVelocity = new double[4];
    public bool spawned = false;
    public List<Cuboidf> OrigCollisionBox = new List<Cuboidf>();
    //private BlockPos minPos = new BlockPos(1);
    //private BlockPos maxPos = new BlockPos(1);
    //private const int dimRadius = 8;
    private BlockPos localOrigin;
    public override bool ApplyGravity => false;//I really didn't expect this to just work as well as it did, still lots of work to do though
    public override bool IsInteractable => true;
    public bool IsRigidBody => true;
    
    public List<OrientedBox> dynamicBoxes = new();
    public EntityVehicle() : base()
    {
    }
    
    public virtual void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        this.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
    }
    
    public static EntityVehicle InitializeVehicle(EntityVehicle entity, BlockyVehicle dim)
    {
        entity.Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.blocks = dim;
        entity.subDimensionIndex = entity.blocks.subDimensionId;
        entity.Pos.SetFrom(entity.blocks.CurrentPos);
        entity.blocks.Dirty = true;
        entity.blocks.TrackSelection = false;
        entity.OrigCollisionBox.Add(new Cuboidf());
        entity.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
        entity.angVelocity = ConvertEulerAngles(0.0f, 0.0f, 0.0f);
        entity.angVelocity[3] = 0;
        entity.Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.WatchedAttributes.SetAttribute("dim", new IntAttribute(dim.subDimensionId));
        entity.tickOffset = dim.subDimensionId % 100;
        
        BlockPos blockPos = new BlockPos(1);
        ((BlockyVehicle)entity.blocks).AdjustPosForSubDimension(ref blockPos);
        //blockPos.X = 0 + dim.subDimensionId % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/;
        //blockPos.Y = 0 + 8192 /*0x2000*/;
        //blockPos.Z = 0 + dim.subDimensionId / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/;
        entity.localOrigin = blockPos;
        //entity.minPos = new BlockPos(blockPos.X - dimRadius + 1, blockPos.Y - dimRadius + 1, blockPos.Z - dimRadius + 1, 1);
        //entity.maxPos = new BlockPos(blockPos.X + dimRadius, blockPos.Y + dimRadius, blockPos.Z + dimRadius, 1);
        entity.spawned = true;
        return entity;
    }

    public static EntityVehicle CreateVehicle(ICoreServerAPI sapi, BlockyVehicle dim)
    {
        EntityVehicle entity = (EntityVehicle) sapi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        return InitializeVehicle(entity, dim);
    }
    
    public static EntityVehicle CreateVehicle(ICoreClientAPI capi, BlockyVehicle dim)
    {
        EntityVehicle entity = (EntityVehicle) capi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        return InitializeVehicle(entity, dim);
    }

    public static IPlayer[] GetNearbyPlayers(ICoreServerAPI sapi, EntityPos entityPos)
    {
        return sapi.World.GetPlayersAround(entityPos.XYZ, (float)sapi.World.DefaultEntityTrackingRange,
            (float)sapi.World.DefaultEntityTrackingRange);
    }
    
    public override void OnGameTick(float dt)
    {
        if (blocks == null || Pos == null)
            Die(EnumDespawnReason.Removed);
        base.OnGameTick(dt);
        if (Api.Side == EnumAppSide.Server)
        {
            tickCounter++;
            if (tickCounter % 100 == tickOffset)//Need a better way to call this when needed
            {
                UpdateBlocks();
            }

            if (tickCounter == 100) tickCounter = 0;
        }
        
        if (spawned)
        {
            if (blocks!.Dirty)
            {
                UpdateBlocks();
            }
            //Api.Logger.Event("Rotation values: "  + qRotation[0] + ", " + qRotation[1] + ", " + qRotation[2] + ", " + qRotation[3]);
            qRotation = ApplyRotation(angVelocity, qRotation, dt);
            float[] angles = Quaterniond.ToEulerAngles(qRotation);
            //Pos.Roll = angles[0];
            //Pos.Yaw = angles[1];
            //Pos.Pitch = angles[2];
            Pos!.Motion.X = 0.01d;
            ((BlockyVehicle)blocks).CurrentPos.SetPos(Pos);//Ensures no desync
            //Api.Logger.Event("World Pos: " + Pos);
        }
    }

    public void SimPhysics(float dt)
    {
        
    }
    
    public override void FromBytes(BinaryReader reader, bool forClient)
    {
        base.FromBytes(reader, forClient);
    }

    public void Dispose()
    {
        if (blocks is BlockyVehicle) ((BlockyVehicle)blocks).Dispose();
    }
    
    private double[] ApplyRotation(double[] angVelocity, double[] rot, double dt)
    {
        double[] output = new double[4];
        double[] out2 = new double[4];
        Quaterniond.Normalize(output, rot);
        Quaterniond.Multiply(out2, angVelocity, output);
        Quaterniond.Scale(out2, out2, dt/2);
        Quaterniond.Add(output, output, out2);
        Quaterniond.Normalize(output, output);
        return output;
    }

    public void UpdateBlocks(CompoundCollider? cachedShapes = null)
    {
        if (this.Api.Side == EnumAppSide.Server)
        {
            IPlayer[] nearbyPlayers = GetNearbyPlayers((ICoreServerAPI)Api, this.Pos);
            if (nearbyPlayers.Length > 0)
            {
                IServerPlayer[] serverPlayerList = new IServerPlayer[nearbyPlayers.Length];
                //If there are players nearby, send a packet to them.
                ((BlockyVehicle)this.blocks).CollectChunksForSending(nearbyPlayers);
                for (int i = 0; i < serverPlayerList.Length; i++)
                {
                    if (nearbyPlayers[i] is IServerPlayer)
                    {
                        serverPlayerList[i] = ((IServerPlayer) nearbyPlayers[i]);
                        //Api.Logger.Event("IPlayer converted to IServerPlayer");
                    }
                }
                ((ICoreServerAPI)Api).Network.GetChannel("VehicleNetworkApi").SendPacket(new VehicleEntityId {entityId = this.EntityId, subDimensionId = this.subDimensionIndex}, serverPlayerList);
            }
        }
        else
        {
            if (!blocks.Dirty) return;//Recalculate the DynamicCollisionBoxes on the client side only if the miniDimension is changed
        }
    }

    public void BlocksDirty()
    {
        this.blocks.Dirty = true;
    }

    public override void OnReceivedServerPos(bool isTeleport)
    {
        //base.OnReceivedServerPos(isTeleport);
        ((BlockyVehicle)blocks).OnReceivedServerPos(isTeleport, this);
    }

    public static double[] ConvertEulerAngles(double pitch, double yaw, double roll)//I think the maths here is wrong
    {
        double[] output = new double[4];
        output[0] = Math.Sin(yaw / 2) * Math.Sin(pitch / 2) * Math.Cos(roll / 2) +
                    Math.Cos(yaw / 2) * Math.Cos(pitch / 2) * Math.Sin(roll / 2);
        output[1] = Math.Sin(yaw / 2) * Math.Cos(pitch / 2) * Math.Cos(roll / 2) +
                    Math.Cos(yaw / 2) * Math.Sin(pitch / 2) * Math.Sin(roll / 2);
        output[2] = Math.Cos(yaw / 2) * Math.Sin(pitch / 2) * Math.Cos(roll / 2) -
                    Math.Sin(yaw / 2) * Math.Cos(pitch / 2) * Math.Sin(roll / 2);
        output[3] = Math.Cos(yaw / 2) * Math.Cos(pitch / 2) * Math.Cos(roll / 2) - 
                    Math.Sin(yaw / 2) * Math.Sin(pitch / 2) * Math.Sin(roll / 2);
        return output;
    }
}