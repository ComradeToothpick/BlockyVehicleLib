using System;
using System.IO;
using System.Runtime.CompilerServices;
using BlockyVehicleLib.Network;
using BlockyVehicleLib.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
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
    public PsuedoCuboidd selectionBox = new PsuedoCuboidd();
    public EntityVehicle() : base()
    {
    }
    
    public virtual void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        this.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
    }

    public static EntityVehicle CreateVehicle(ICoreServerAPI sapi, BlockAccessorMovable dim)
    {
        EntityVehicle entity = VehicleCreateAndLinkWithDimension(sapi, dim);
        entity.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
        entity.angVelocity = PsuedoCuboidd.ConvertEulerAngles(0.1f, 0.1f, 0.1f);
        entity.angVelocity[3] = 0;
        ((RegistryObject) entity).Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.WatchedAttributes.SetAttribute("dim", (IAttribute) new IntAttribute(dim.subDimensionId));
        entity.tickOffset = dim.subDimensionId % 100;
        entity.spawned = true;
        return entity;
    }
    public static EntityVehicle VehicleCreateAndLinkWithDimension(
        ICoreServerAPI sapi,
        IMiniDimension dimension)
    {
        EntityVehicle entity = (EntityVehicle) sapi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        entity.Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.AssociateWithDimension(dimension);
        return entity;
    }
    
    public static EntityVehicle CreateVehicle(ICoreClientAPI capi, IMiniDimension dim)
    {
        EntityVehicle entity = (EntityVehicle) capi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        entity.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
        entity.angVelocity = PsuedoCuboidd.ConvertEulerAngles(0.1f, 0.1f, 0.1f);
        entity.angVelocity[3] = 0;
        ((RegistryObject) entity).Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.WatchedAttributes.SetAttribute("dim", (IAttribute) new IntAttribute(dim.subDimensionId));
        entity.AssociateWithDimension(dim);
        entity.tickOffset = dim.subDimensionId % 100;
        entity.spawned = true;
        return entity;
    }
    
    public static EntityVehicle CreateVehicle(ICoreClientAPI capi, BlockAccessorMovable dim)
    {
        EntityVehicle entity = (EntityVehicle) capi.World.ClassRegistry.CreateEntity("blockyvehiclelib.vehicle");
        entity.qRotation = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
        entity.angVelocity = PsuedoCuboidd.ConvertEulerAngles(0.1f, 0.1f, 0.1f);
        entity.angVelocity[3] = 0;
        ((RegistryObject) entity).Code = new AssetLocation("blockyvehiclelib:vehicle");
        entity.WatchedAttributes.SetAttribute("dim", (IAttribute) new IntAttribute(dim.subDimensionId));
        entity.AssociateWithDimension(dim);
        entity.tickOffset = dim.subDimensionId % 100;
        entity.spawned = true;
        return entity;
    }

    public static IPlayer[] GetNearbyPlayers(ICoreServerAPI sapi, EntityPos entityPos)
    {
        return sapi.World.GetPlayersAround(entityPos.XYZ, (float)sapi.World.DefaultEntityTrackingRange,
            (float)sapi.World.DefaultEntityTrackingRange);
    }
    
    public override void OnGameTick(float dt)
    {
        if (this.blocks == null || ((Entity) this).Pos == null)
            this.Die(EnumDespawnReason.Removed, (DamageSource) null);
        base.OnGameTick(dt);
        if (Api.Side == EnumAppSide.Server)
        {
            tickCounter++;
            if (tickCounter % 100 == this.tickOffset)
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

            if (tickCounter == 100) tickCounter = 0;
        }
        //rotation happens around the midpoint of the +X +Z Vertical line of the cuboid
        //would like to fix that, but low priority atm
        if (spawned)
        {
            //Api.Logger.Event("Rotation values: "  + qRotation[0] + ", " + qRotation[1] + ", " + qRotation[2] + ", " + qRotation[3]);
            qRotation = ApplyRotation(angVelocity, qRotation, dt);
            float[] angles = Quaterniond.ToEulerAngles(qRotation);
            this.Pos.Roll = angles[0];
            this.Pos.Yaw = angles[1];
            this.Pos.Pitch = angles[2];
        }
        /*
        ((Entity) this).Pos.Motion.X = 0.01;
        ((Entity) this).Pos.Y = (double) (int) ((Entity) this).Pos.Y + 0.5;
        ((Entity) this).Pos.Yaw = (float) (((Entity) this).Pos.X % 6.3) / 20f;
        ((Entity) this).Pos.Pitch = (float) GameMath.Sin(((Entity) this).Pos.X % 6.3) / 5f;
        ((Entity) this).Pos.Roll = (float) GameMath.Sin(((Entity) this).Pos.X % 12.6) / 3f;
        */
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
}