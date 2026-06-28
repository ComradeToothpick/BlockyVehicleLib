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
    private BlockPos minPos = new BlockPos(1);
    private BlockPos maxPos = new BlockPos(1);
    private const int dimRadius = 8;
    private BlockPos localOrigin;
    

    public EntityVehicle() : base()
    {
    }
    
    public virtual void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        this.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
    }
    
    public static EntityVehicle InitializeVehicle(EntityVehicle entity, BlockAccessorMovable dim)
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
        entity.WatchedAttributes.SetAttribute("dim", (IAttribute) new IntAttribute(dim.subDimensionId));
        entity.tickOffset = dim.subDimensionId % 100;
        BlockPos blockPos = new BlockPos(1);
        ((BlockyVehicle)entity.blocks).AdjustPosForSubDimension(ref blockPos);
        //blockPos.X = 0 + dim.subDimensionId % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/;
        //blockPos.Y = 0 + 8192 /*0x2000*/;
        //blockPos.Z = 0 + dim.subDimensionId / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/;
        entity.localOrigin = blockPos;
        entity.minPos = new BlockPos(blockPos.X - dimRadius + 1, blockPos.Y - dimRadius + 1, blockPos.Z - dimRadius + 1, 1);
        entity.maxPos = new BlockPos(blockPos.X + dimRadius, blockPos.Y + dimRadius, blockPos.Z + dimRadius, 1);
        entity.spawned = true;
        return entity;
    }

    public static EntityVehicle CreateVehicle(ICoreServerAPI sapi, BlockAccessorMovable dim)
    {
        EntityVehicle entity = (EntityVehicle) sapi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        return InitializeVehicle(entity, dim);
    }
    
    public static EntityVehicle CreateVehicle(ICoreClientAPI capi, BlockAccessorMovable dim)
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
            if (tickCounter % 100 == tickOffset)
            {
                UpdateBlocks();
            }

            if (tickCounter == 100) tickCounter = 0;
        }
        
        if (spawned)
        {
            if (blocks.Dirty)
            {
                UpdateBlocks();
            }
            //Api.Logger.Event("Rotation values: "  + qRotation[0] + ", " + qRotation[1] + ", " + qRotation[2] + ", " + qRotation[3]);
            qRotation = ApplyRotation(angVelocity, qRotation, dt);
            float[] angles = Quaterniond.ToEulerAngles(qRotation);
            //Pos.Roll = angles[0];
            //Pos.Yaw = angles[1];
            //Pos.Pitch = angles[2];
            Pos.Motion.X = 0.01d;
            //if (Pos.X > blocks.selectionTrackingOriginalPos.X + 1.5f) blocks.selectionTrackingOriginalPos.X += 1;
            ((BlockyVehicle)blocks).CurrentPos.SetPos(Pos);


            //this.blocks.CurrentPos = this.Pos;
            //this.blocks.CurrentPos =  this.Pos;
            //this.Pos.X += this.Pos.Motion.X * dt;
            //this.Pos.Y += this.Pos.Motion.Y * dt;
            //this.Pos.Z += this.Pos.Motion.Z * dt;
        }
        /*
        ((Entity) this).Pos.Motion.X = 0.01;
        ((Entity) this).Pos.Y = (double) (int) ((Entity) this).Pos.Y + 0.5;
        ((Entity) this).Pos.Yaw = (float) (((Entity) this).Pos.X % 6.3) / 20f;
        ((Entity) this).Pos.Pitch = (float) GameMath.Sin(((Entity) this).Pos.X % 6.3) / 5f;
        ((Entity) this).Pos.Roll = (float) GameMath.Sin(((Entity) this).Pos.X % 12.6) / 3f;
        */
    }

    public void OnPhysicsTick(float dt)
    {
        
    }
    
    public override void FromBytes(BinaryReader reader, bool forClient)
    {
        base.FromBytes(reader, forClient);
    }

    public void Dispose()
    {
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

    public BuiltCompound? GetShape()
    {
        return null;
        Api.Logger.Event("GetShape is executing");
        DynamicPhysicsBehaviour? behaviour = this.GetBehavior<DynamicPhysicsBehaviour>();
        if (behaviour == null)
        {
            Api.Logger.Event("DynamicPhysicsBehaviour is null");
            return null;
        }

        if (behaviour.VehicleChildBoxes == null)
        {
            Api.Logger.Event("behaviour.VehicleChildBoxes is null, creating a new list");
            behaviour.VehicleChildBoxes = new List<ManualChildBox>();
        }
        //walk through the blocks in the minidimension, collect the shapes of the blocks and compile them together
        if (this.blocks == null)
        {
            Api.Logger.Event("blocks (BlockAccessorMovable) is null");
            return null;
        }

        if (this.blocks.Dirty && Api.Side == EnumAppSide.Server)
        {
            Api.Logger.Event("blocks are dirty, calling UpdateBlocks");
            UpdateBlocks();
        }
        IBlockAccessor blockAccessor = this.blocks;
        BuiltCompound cachedShapes = new BuiltCompound();
        cachedShapes.ManualChildBoxes = new List<ManualChildBox>();
        //blockAccessor.WalkBlocks(this.minPos, this.maxPos, (Action<Block, int, int, int>)((block, x, y, z) =>
        for (int x = minPos.X; x <= maxPos.X; x++)
        for (int y =  minPos.Y; y <= maxPos.Y; y++)
        for (int z =  minPos.Z; z <= maxPos.Z; z++)
        {
            BlockPos blockPos = new BlockPos(x, y, z, 1);//Have to do it this way as WalkBlocks is not dimensionally aware
            Block block = blockAccessor.GetBlock(blockPos);
            if (block.BlockId != 0)
            {
                CompositeShape shape = block.Shape.Clone();
                Api.Logger.Event("Block Detected: " + block.BlockId);
                ManualChildBox box1 = new ManualChildBox()
                {
                    HalfExtents = new Vector3(shape.Scale/2),
                    LocalOrientation = Quaternion.CreateFromYawPitchRoll(shape.rotateY, shape.rotateX, shape.rotateZ),
                    LocalPosition = new Vector3(shape.offsetX + x - localOrigin.X, shape.offsetY + y - localOrigin.Y, shape.offsetZ + z - localOrigin.Z)
                };
                behaviour.VehicleChildBoxes.Add(box1);
                foreach (CompositeShape b in shape.Overlays)
                {
                    ManualChildBox box = new ManualChildBox()
                    {
                        HalfExtents = new Vector3(b.Scale/2),
                        LocalOrientation = Quaternion.CreateFromYawPitchRoll(b.rotateY, b.rotateX, b.rotateZ),
                        LocalPosition = new Vector3(b.offsetX + x - localOrigin.X, b.offsetY + y - localOrigin.Y, b.offsetZ + z - localOrigin.Z)
                    };
                    behaviour.VehicleChildBoxes.Add(box);
                }
            }
            else
            {
                //Api.Logger.Event("Air detected at: ({0}, {1}, {2})",  x, y, z);
            }
        }//), true);
        cachedShapes.ManualChildBoxes.AddRange(behaviour.VehicleChildBoxes);
        ((BlockAccessorMovable)this.blocks).RecalculateCenterOfMass(Api.World);
        cachedShapes.LocalCenterOfMassOffset = new Vector3((float)(((BlockAccessorMovable)this.blocks).CenterOfMass.X), (float)((BlockAccessorMovable)this.blocks).CenterOfMass.Y, (float)((BlockAccessorMovable)this.blocks).CenterOfMass.Z);
        return cachedShapes;
    }

    public void UpdateBlocks(BuiltCompound? cachedShapes = null)
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