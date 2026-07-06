using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace BlockyVehicleLib;

public struct PositionSnapshot
{
    public double x;
    public double y;
    public double z;

    public float interval;

    public bool isTeleport;

    public PositionSnapshot(Vec3d pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        this.isTeleport = isTeleport;
    }

    public PositionSnapshot(EntityPos pos, float interval, bool isTeleport)
    {
        x = pos.X;
        y = pos.Y;
        z = pos.Z;

        this.interval = interval;
        this.isTeleport = isTeleport;
    }
}

public class BlockyVehicle : BlockAccessorMovable, IMiniDimension, IRenderer
{
    ICoreAPI api;//Cool Idea, have the base entity be an entityAgent and have a second miniDimension that gets used like a head
    private BlockyVehicleLibModSystem system;
    public Vec3f innerPos;
    public double RenderOrder => 0;
    public int RenderRange => 9999;
    
    public float dtAccum = 0;

    // Will lerp from pL to pN.
    public PositionSnapshot pL;
    public PositionSnapshot pN;

    public Queue<PositionSnapshot> positionQueue = new();
    
    public void PushQueue(PositionSnapshot snapshot)
    {
        positionQueue.Enqueue(snapshot);
        queueCount++;
    }
    
    // Interval at what things should be received.
    public const float interval = 1 / 15f;
    public int queueCount;
    
    public void Initialize(Entity entity)
    {
        if (api.Side == EnumAppSide.Server) return;
        PushQueue(new PositionSnapshot(entity.Pos, 0, false));
    }
    
    public void PopQueue(bool clear)
    {
        dtAccum -= pN.interval;

        if (dtAccum < 0) dtAccum = 0;
        if (dtAccum > 1) dtAccum = 0;

        pL = pN;
        pN = positionQueue.Dequeue();
        queueCount--;

        // Clear flooded queue.
        if (clear && queueCount > 1) PopQueue(true);

        CurrentPos.SetPos(pN.x, pN.y, pN.z);
        //physics?.HandleRemotePhysics(Math.Max(pN.interval, interval), pN.isTeleport);//I don't think I need this
    }
    
    /// <summary>
    /// Called when the client receives a new position.
    /// Move the positions forward and reset the accumulation.
    /// </summary>
    public void OnReceivedServerPos(bool isTeleport, Entity entity)
    {
        float tickInterval = entity.Attributes.GetInt("tickDiff", 1) * interval;

        PushQueue(new PositionSnapshot(entity.Pos, tickInterval, isTeleport));

        if (isTeleport)
        {
            dtAccum = 0;
            positionQueue.Clear();
            queueCount = 0;

            PushQueue(new PositionSnapshot(CurrentPos, tickInterval, false));
            PushQueue(new PositionSnapshot(CurrentPos, tickInterval, false));

            PopQueue(false);
            PopQueue(false);
        }
        if (queueCount > 20)
        {
            PopQueue(true);
        }
    }

    public int wait = 0;
    public float targetSpeed = 0.6f;
    
    //private Dictionary<long, IWorldChunk>? _chunks;
    public BlockyVehicle(BlockAccessorBase parent, Vec3d pos, ICoreAPI api, Vec3f? innerPos = null) : base(parent, pos)
    {
        this.innerPos = innerPos ?? Vec3f.Zero;
        this.CurrentPos = new EntityPos(pos.X, pos.Y, pos.Z);
        this.api = api;
        this.TrackSelection = true;
        this.system = api.ModLoader.GetModSystem<BlockyVehicleLibModSystem>();
        if (api.Side == EnumAppSide.Client) ((ICoreClientAPI)api).Event.RegisterRenderer(this, EnumRenderStage.Before, "interpolatepositionvehicle");
    }
    
    public void AdjustPosForSubDimension(ref BlockPos pos)
    {
        pos.X += subDimensionId % 4096 * 16384 + 8192;
        pos.Y += 8192;
        pos.Z += subDimensionId / 4096 * 16384 + 8192;
    }
    
    public override FastVec3d GetRenderOffset(float dt)
    {
        FastVec3d fastVec3d = new FastVec3d(-(subDimensionId % 4096) * 16384, 0.0, -(subDimensionId / 4096 * 16384));
        fastVec3d = fastVec3d.Add(-8192.0);
        return fastVec3d.Add(CurrentPos.X - 0.5, CurrentPos.InternalY, CurrentPos.Z - 0.5);
    }

    public bool CallSetSolidBlock(
        int blockId,
        BlockPos pos,
        IWorldChunk chunk,
        ItemStack byItemstack)
    {
        return SetSolidBlock(blockId, pos, chunk, byItemstack);
    }

    public void Dispose()
    {
        if (api is ICoreClientAPI capi) capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (api.Side == EnumAppSide.Server) return;
        if (((ICoreClientAPI)api).IsGamePaused) return;

        if (queueCount < wait)
        {
            return;
        }

        dtAccum += dt * targetSpeed;

        while (dtAccum > pN.interval)
        {
            if (queueCount > 0)
            {
                PopQueue(false);
                wait = 0;
            }
            else
            {
                wait = 1;
                break;
            }
        }

        float speed = (queueCount * 0.2f) + 0.8f;
        targetSpeed = GameMath.Lerp(targetSpeed, speed, dt * 4);

        float delta = dtAccum / pN.interval;
        if (wait != 0) delta = 1;
        
        CurrentPos.X = GameMath.Lerp(pL.x, pN.x, delta);
        CurrentPos.Y = GameMath.Lerp(pL.y, pN.y, delta);
        CurrentPos.Z = GameMath.Lerp(pL.z, pN.z, delta);
    }
}