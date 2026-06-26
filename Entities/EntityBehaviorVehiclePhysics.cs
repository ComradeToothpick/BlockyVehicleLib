using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BlockyVehicleLib.Util;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace BlockyVehicleLib.Entities;


/// <summary>
/// Player physics for vehicles. This class has no further properties.
/// <br/>Uses the "entityvehiclephysics" code
/// </summary>
/// <example><code lang="json">
/// "behaviors": [
///  {
///     "code": "entityvehiclephysics"
///  }
/// ]
/// </code></example>
[DocumentAsJson]
public class EntityBehaviorVehiclePhysics(Entity entity) : 
    EntityControlledVehiclePhysics(entity),
    IPhysicsTickable,
    IRemotePhysics,
    IRenderer,
    IDisposable
{
    private IPlayer player;
    private IServerPlayer serverPlayer;
    private EntityPlayer entityPlayer;
    
    // 60/s client-side updates.
    private const float interval = 1 / 60f;
    private float accum = 0;
    private int currentTick;

    //public double RenderOrder => 1;

    //public int RenderRange => 9999;

    private int prevDimension = 0;
    public const float ClippingToleranceOnDimensionChange = 0.0625f;
    protected readonly Vec3d prevPos = new Vec3d();
    protected double motionBeforeY;
    protected bool feetInLiquidBefore;
    protected bool onVehicleBefore;
    protected bool swimmingBefore;
    protected bool collidedVehicleBefore;
    private Vec3d newPos = new Vec3d();
    protected bool vehiclesNearby = false;
    protected int tickCounter = 0;
    
    /// <summary>The amount of drag while travelling through water.</summary>
    //public double WaterDragValue = (double) GlobalConstants.WaterDrag;
    /// <summary>The amount of drag while travelling through the air.</summary>
    //public double AirDragValue = (double) GlobalConstants.AirDragAlways;
    /// <summary>The amount of drag while travelling on the ground.</summary>
    //public double GroundDragValue = 0.699999988079071;
    /// <summary>The amount of drag while travelling on the ground.</summary>
    //public double BoyancyMul = 1.0;
    /// <summary>
    /// The amount of gravity applied per tick to this entity.
    /// </summary>
    //public double GravityPerSecond = (double) GlobalConstants.GravityPerSecond;
    /// <summary>
    /// If set, will test for entity collision every tick (expensive)
    /// </summary>
    public Action<float> OnPhysicsTickCallback;
    [ThreadStatic] private static BlockPos tmpPos;

    public Entity Entity => this.entity;

    public bool Ticking { get; set; } = true;

    public override string PropertyName() => "entityvehiclephysics";
  
    //private EntityVehicle[] nearbyVehicles = new EntityVehicle[10];//Need to find a resource efficient way to keep this up to date, limit of 10 for now
    private Dictionary<int, EntityVehicle> nearbyVehiclesList = new Dictionary<int, EntityVehicle>();
    
    public void SetState(EntityPos pos)
    {
        this.prevPos.Set(pos);
        this.motionBeforeY = pos.Motion.Y;
        Entity entity = this.Entity;
        this.onVehicleBefore = entity.OnGround;
        this.feetInLiquidBefore = entity.FeetInLiquid;
        this.swimmingBefore = entity.Swimming;
        this.collidedVehicleBefore = entity.Collided;
    }

    public override void SetModules()
    {
        physicsModules.Add(new PModuleOnGround());
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        entityPlayer = entity as EntityPlayer;
        base.Initialize();
        vCollisionTester = new CachingVehicleCollisionTester();//If this works, I don't need a harmony patch for this
        this.SetProperties(properties, attributes);
    
        if (this.Entity.Api is ICoreServerAPI api)
        {
            sapi.Logger.Event("EntityBehaviorVehiclePhysics Initializing");
            //api.Server.AddPhysicsTickable((IPhysicsTickable) this);
            //Leave this as non-tickable on server side, calculate and send to server from client side
        }
        else
        {
            EnumHandling handled = EnumHandling.Handled;
            this.OnReceivedServerPos(true, ref handled);
        }
        entity.PhysicsUpdateWatcher?.Invoke(0, entity.Pos.XYZ);
    }

    public void OnReceivedClientPos(int version)
    {
        serverPlayer ??= entityPlayer.Player as IServerPlayer;

        if (version > previousVersion)
        {
            previousVersion = version;
            HandleRemotePhysics(clientInterval, true);
            return;
        }

        HandleRemotePhysics(clientInterval, false);
    }

  
    //This probably needs to be updated to apply tests for each relativePos for each (nearby) vehicle
    public void HandleRemotePhysics(float dt, bool isTeleport)
    {
        player ??= entityPlayer.Player;

        if (player == null) return;
        var entity = this.entity;
        
        if (nPos == null)
        {
            nPos = new Vec3d();
            nPos.Set(Entity.Pos);
        }
    
        float dtFactor = dt * 60f;
        EntityPos lPos = this.lPos;
        
        lPos.SetFrom(nPos);
        nPos.Set(entity.Pos);
        lPos.Dimension = entity.Pos.Dimension;
        
        if (isTeleport)
        {
            lPos.SetFrom(nPos);
        }
        
        lPos.Motion.X = (nPos.X - lPos.X) / dtFactor;
        lPos.Motion.Y = (nPos.Y - lPos.Y) / dtFactor;
        lPos.Motion.Z = (nPos.Z - lPos.Z) / dtFactor;
        
        if (lPos.Motion.Length() > 20.0) lPos.Motion.Set(0.0, 0.0, 0.0);
        
        entity.Pos.Motion.Set(lPos.Motion);
        
        vCollisionTester.NewTick(lPos);
        
        EntityAgent eagent = entity as EntityAgent;
        if (eagent.MountedOn != null)
        {
            entity.Swimming = false;
            entity.OnGround = false;

            if (capi != null)
            {
                entity.Pos.SetPos(eagent.MountedOn.SeatPosition);
            }

            entity.Pos.Motion.X = 0;
            entity.Pos.Motion.Y = 0;
            entity.Pos.Motion.Z = 0;

            // No-clip detection.
            if (sapi != null)
            {
                vCollisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
            }
            return;
        }
        
        SetState(lPos, dt);
        
        
        EntityControls controls = eagent.Controls;
        if (!controls.NoClip)
        {
            if (sapi != null)
            {
                vCollisionTester.ApplyTerrainCollision(entity, lPos, dtFactor, ref newPos, 0, 0);
            }

            RemoteMotionAndCollision(lPos, dtFactor);
            ApplyTests(lPos, eagent.Controls, dt, true);
        } else
        {
            var pos = entity.Pos;

            pos.X += pos.Motion.X * dt * 60f;
            pos.Y += pos.Motion.Y * dt * 60f;
            pos.Z += pos.Motion.Z * dt * 60f;
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;
            controls.Gliding = false;
        }
    }
    
    public override void OnPhysicsTick(float dt)
    {
        SimPhysics(dt, ((EntityBehavior)this).entity.Pos);
        /*
        Entity entity = this.Entity;
        if (entity.State != EnumEntityState.Active || !this.Ticking)
        {
            return;
        }
        if (entity.Api.Side == EnumAppSide.Server)
        {
            if (tickCounter % 4 == 0)
            {
                //this.nearbyVehiclesList = GetNearbyVehicles(entity);
                if (nearbyVehiclesList.Count > 0)
                {
                    vehiclesNearby = true;
                    for (int i = 1; i < nearbyVehiclesList.Count; i++)
                    {
                        vehiclePosList[i] = nearbyVehiclesList.Values.ElementAt(i).Pos;
                    }
                }
                else vehiclesNearby = false;
                tickCounter = 0;
                sapi.Logger.Event("Vehicles Nearby: " + vehiclesNearby.ToString());
            }
        }
        
        IMountable mountableSupplier = this.mountableSupplier;
        if ((mountableSupplier != null ? (mountableSupplier.IsBeingControlled() ? 1 : 0) : 0) != 0 && entity.World.Side == EnumAppSide.Server)
            return;
        EntityPos pos = entity.Pos;
        //PhysicsBehaviorBaseVehicle.collisionTester.AssignToEntity((PhysicsBehaviorBaseVehicle) this, pos.Dimension);
        int num = pos.Motion.Length() > 0.1 ? 10 : 1;
        float dt1 = dt / (float) num;
        for (int index = 0; index < num; ++index)
        {
            this.SetState(pos);
            this.MotionAndCollision(pos, dt1, vehiclesNearby);//if no vehicles are nearby, this should get skipped
            this.ApplyTests(pos);
        }
        entity.Pos.SetFrom(pos);
        */
    }
    
    public override void OnGameTick(float deltaTime)
    {
        // Player physics is called only client side, but we still need to call Block.OnEntityInside and other usual server-side AfterPhysicsTick things
        if (entity.World is IServerWorldAccessor)
        {
            callOnEntityInside();
            entity.AfterPhysicsTick?.Invoke();
        }

        // note: no need to invoke AfterPhysicsTick on the client, as client-side it will be called from this behavior's OnRenderFrame() method
    }
    
    public void SimPhysics(float dt, EntityPos pos)
    {
        var entity = this.entity;
        if (entity.State != EnumEntityState.Active) return;
        player ??= entityPlayer.Player;
        if (player == null) return;

        EntityAgent eagent = entity as EntityAgent;
        EntityControls controls = eagent.Controls;

        // Set previous pos to be used for camera callback.
        prevPos.Set(pos);
        tmpPos.dimension = pos.Dimension;

        SetState(pos, dt);
        SetPlayerControls(pos, controls, dt);

        // If mounted on something, set position to it and return.
        if (eagent.MountedOn != null)
        {
            entity.Swimming = false;
            entity.OnGround = false;

            pos.SetPos(eagent.MountedOn.SeatPosition);

            pos.Motion.X = 0;
            pos.Motion.Y = 0;
            pos.Motion.Z = 0;
            return;
        }

        MotionAndCollision(pos, controls, dt);
        if (!controls.NoClip)
        {
            vCollisionTester.NewTick(pos);

            if (prevDimension != pos.Dimension)
            {
                prevDimension = pos.Dimension;

                // Dimension changes are allowed a small amount of clipping into terrain, so we need to push out on the client here, we add 20% for rounding/sync errors
                vCollisionTester.PushOutFromBlocks(entity.World.BlockAccessor, entity, pos.XYZ, ClippingToleranceOnDimensionChange * 1.2f);
            }

            ApplyTests(pos, controls, dt, false);

            // Attempt to stop gliding/flying.
            if (controls.Gliding)
            {
                if (entity.Collided || entity.FeetInLiquid || !entity.Alive || player.WorldData.FreeMove || controls.IsClimbing)
                {
                    controls.GlideSpeed = 0;
                    controls.Gliding = false;
                    controls.IsFlying = false;
                    entityPlayer.WalkPitch = 0;
                }
            }
            else
            {
                controls.GlideSpeed = 0;
            }
        } else
        {
            pos.X += pos.Motion.X * dt * 60f;
            pos.Y += pos.Motion.Y * dt * 60f;
            pos.Z += pos.Motion.Z * dt * 60f;
            entity.Swimming = false;
            entity.FeetInLiquid = false;
            entity.OnGround = false;
            controls.Gliding = false;

            prevDimension = pos.Dimension;   // If NoClip is enabled we don't care about dimension changes either
        }
    }
    
    //unsure if this needs to be changed at all at this point, there is potential to create a new EntityControls type to handle the physics when on a vehicle maybe?
    public void SetPlayerControls(EntityPos pos, EntityControls controls, float dt)
    {
        IClientWorldAccessor clientWorld = entity.World as IClientWorldAccessor;
        // We pretend the entity is flying to disable gravity so that EntityBehaviorInterpolatePosition system
        // can work better   (see commit 09003c0c)
        controls.IsFlying = player.WorldData.FreeMove || (clientWorld != null && clientWorld.Player.ClientId != player.ClientId) && !controls.IsClimbing;
        controls.NoClip = player.WorldData.NoClip;
        controls.MovespeedMultiplier = player.WorldData.MoveSpeedMultiplier;

        if (controls.Gliding && !controls.IsClimbing)
        {
            controls.IsFlying = true;
        }

        if ((controls.TriesToMove || controls.Gliding) && player is IClientPlayer clientPlayer)
        {
            float prevYaw = pos.Yaw;
            pos.Yaw = (entity.Api as ICoreClientAPI).Input.MouseYaw;

            if (entity.Swimming || controls.Gliding)
            {
                float prevPitch = pos.Pitch;
                pos.Pitch = clientPlayer.CameraPitch;
                controls.CalcMovementVectors(pos, dt);
                pos.Yaw = prevYaw;
                pos.Pitch = prevPitch;
            }
            else
            {
                controls.CalcMovementVectors(pos, dt);
                pos.Yaw = prevYaw;
            }

            float desiredYaw = (float)Math.Atan2(controls.WalkVector.X, controls.WalkVector.Z);
            float yawDist = GameMath.AngleRadDistance(entityPlayer.WalkYaw, desiredYaw);

            entityPlayer.WalkYaw += GameMath.Clamp(yawDist, -6 * dt * GlobalConstants.OverallSpeedMultiplier, 6 * dt * GlobalConstants.OverallSpeedMultiplier);
            entityPlayer.WalkYaw = GameMath.Mod(entityPlayer.WalkYaw, GameMath.TWOPI);

            if (entity.Swimming || controls.Gliding)
            {
                float desiredPitch = -(float)Math.Sin(pos.Pitch);
                float pitchDist = GameMath.AngleRadDistance(entityPlayer.WalkPitch, desiredPitch);
                entityPlayer.WalkPitch += GameMath.Clamp(pitchDist, -2 * dt * GlobalConstants.OverallSpeedMultiplier, 2 * dt * GlobalConstants.OverallSpeedMultiplier);
                entityPlayer.WalkPitch = GameMath.Mod(entityPlayer.WalkPitch, GameMath.TWOPI);
            }
            else
            {
                entityPlayer.WalkPitch = 0;
            }
        }
        else
        {
            if (!entity.Swimming && !controls.Gliding)
            {
                entityPlayer.WalkPitch = 0;
            }
            else if (entity.OnGround && entityPlayer.WalkPitch != 0)
            {
                entityPlayer.WalkPitch = GameMath.Mod(entityPlayer.WalkPitch, GameMath.TWOPI);
                if (entityPlayer.WalkPitch < 0.01f || entityPlayer.WalkPitch > GameMath.PI - 0.01f)   // Without the PI test, the player can backflip 360 degrees, due to WalkPitch starting in the range PI to TWOPI  (typically just fractionally less than TWOPI)
                {
                    entityPlayer.WalkPitch = 0;
                }
                else // Slowly revert player to upright position if feet touched the bottom of water.
                {
                    entityPlayer.WalkPitch -= GameMath.Clamp(entityPlayer.WalkPitch, 0, 1.2f * dt * GlobalConstants.OverallSpeedMultiplier);

                    if (entityPlayer.WalkPitch < 0) entityPlayer.WalkPitch = 0;
                }
            }

            float prevYaw = pos.Yaw;
            controls.CalcMovementVectors(pos, dt);
            pos.Yaw = prevYaw;
        }
    }

    new public void RemoteMotionAndCollision(EntityPos pos, float dtFactor)
    {
        if (vehiclesNearby) PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(this.entity, pos, this.vehiclePosList/*EntityVehiclePosList*/, dtFactor, ref this.nPos, this.subDimensionIdList/*subDimensionId*/, 0.0f, this.CollisionYExtra);
        ((EntityBehavior) this).entity.OnGround = ((EntityBehavior) this).entity.CollidedVertically & this.lPos.Motion.Y < 0.0;
        pos.SetPos(this.nPos);
    }

    public void MotionAndCollision(EntityPos pos, float dt, bool vehiclesNearby)
    {
        if (!vehiclesNearby || nearbyVehiclesList.Count == 0) return;
        float dtFactor = 60f * dt;
        Entity entity = this.Entity;
        Vec3d motion = pos.Motion;
        IBlockAccessor blockAccessor = entity.World.BlockAccessor;
        int dimension = 1;
        //Removed drag
        if (this.onVehicleBefore)
        {
            if (motion.HorLength() < 1E-05)
            {
                motion.X = 0.0;
                motion.Z = 0.0;
            }
        }
        Block block = (Block) null;

        if (vehiclesNearby) 
        {
            subDimensionIdList = new int[nearbyVehiclesList.Count];
            for (int i = 1; i < nearbyVehiclesList.Count; i++)
            {
                subDimensionIdList[i] = nearbyVehiclesList.Keys.ElementAt(i);
                EntityPos tmpEntPos = nearbyVehiclesList.Values.ElementAt(i).Pos;
                double[] angVel = nearbyVehiclesList.Values.ElementAt(i).angVelocity;
                EntityPos convPos = GetConvertedPos(tmpEntPos, pos, subDimensionIdList[i]);
                Cuboidd intoBox = new Cuboidd();
                bool colliding = PhysicsBehaviorBaseVehicle.vCollisionTester.GetCollidingCollisionBox(blockAccessor, entity.CollisionBox,
                    convPos, ref intoBox, subDimensionIdList[i], nearbyVehiclesList.Values.ElementAt(i).qRotation);
                motion = convPos.Motion;
                
                double x = motion.X * (double) dtFactor + convPos.X;
                double y = motion.Y * (double) dtFactor + convPos.Y;
                double z = motion.Z * (double) dtFactor + convPos.Z;
                Vec3d newPos = this.newPos;
                //These only check the world border, not very useful
                /*
                if (blockAccessor.IsNotTraversable((double)(int)x, (double)(int)convPos.Y, (double)(int)convPos.Z, 1))
                {
                    newPos.X = pos.X;
                    this.Entity.Api.Logger.Event("X Collision detected");
                }
                */
                pos.SetPos(newPos);
                if (x < newPos.X && motion.X < 0.0 || x > newPos.X && motion.X > 0.0)
                    motion.X = 0.0;
                if (y < newPos.Y && motion.Y < 0.0 || y > newPos.Y && motion.Y > 0.0)
                    motion.Y = 0.0;
                if ((z >= newPos.Z || motion.Z >= 0.0) && (z <= newPos.Z || motion.Z <= 0.0))
                    return;
                motion.Z = 0.0;
            }
            this.applyCollision(pos, dtFactor);
        }
        //removed redundant physics
        /*
        Vec3d newPos = this.newPos;
        if (blockAccessor.IsNotTraversable((double) (int) x, (double) (int) pos.Y, (double) (int) pos.Z, dimension))
          newPos.X = pos.X;
        if (blockAccessor.IsNotTraversable((double) (int) pos.X, (double) (int) y, (double) (int) pos.Z, dimension))
          newPos.Y = pos.Y;
        if (blockAccessor.IsNotTraversable((double) (int) pos.X, (double) (int) pos.Y, (double) (int) z, dimension))
          newPos.Z = pos.Z;
        pos.SetPos(newPos);
        if (x < newPos.X && motion.X < 0.0 || x > newPos.X && motion.X > 0.0)
          motion.X = 0.0;
        if (y < newPos.Y && motion.Y < 0.0 || y > newPos.Y && motion.Y > 0.0)
          motion.Y = 0.0;
        if ((z >= newPos.Z || motion.Z >= 0.0) && (z <= newPos.Z || motion.Z <= 0.0))
          return;
        motion.Z = 0.0;
        */
    }

    protected virtual void applyCollision(EntityPos pos, float dtFactor)
    {
        //Should skip if no vehicles are nearby
        if (vehiclesNearby)
        {
            //int[] test = new int[1];
            //test[0] = 0;
            //EntityPos[] posTest = new EntityPos[1];
            //posTest[0] = new EntityPos(10, 10, 10);
            //if (this.Entity == null) sapi.Logger.Error("Entity is null");
            //if (pos == null) sapi.Logger.Error("pos is null");
            //if (this.vehiclePosList == null) sapi.Logger.Error("vehiclePosList is null");
            //if (dtFactor == null) sapi.Logger.Error("dtFactor is null");
            //if (this.newPos == null) sapi.Logger.Error("newPos is null");
            //if (this.subDimensionIdList == null) sapi.Logger.Error("subDimensionIdList is null");//this one should be fixed?
            //if (this.CollisionYExtra == null) sapi.Logger.Error("CollisionYExtra is null");
            //if (vCollisionTester == null)  sapi.Logger.Error("vCollisionTester is null");
            PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(
                this.Entity, 
                pos, 
                this.vehiclePosList/*EntityVehiclePosList*/, 
                dtFactor, 
                ref this.newPos, 
                subDimensionIdList/*subDimensionId*/, 
                0.0f, 
                this.CollisionYExtra);
        }
    }

    //this needs to be updated to apply tests for each relativePos for each (nearby) vehicle
    public void ApplyTests(EntityPos pos)
    {
        Entity entity = this.Entity;
        GetNearbyVehicles(entity);
        IBlockAccessor blockAccessor = entity.World.BlockAccessor;
        bool falling = pos.Motion.Y <= 0.0;
        entity.OnGround = entity.CollidedVertically && falling;
        //Removed redundant physics
        if (!this.collidedVehicleBefore && entity.Collided)
        {
            entity.OnCollided();
        }
        
        PsuedoCuboidd entityBox = PhysicsBehaviorBaseVehicle.vCollisionTester.sudoBox;
      
        foreach (int key in nearbyVehiclesList.Keys)
        {
            int dim = key;
            EntityPos convPos = GetConvertedPos(nearbyVehiclesList.TryGetValue(key).Pos, entity.Pos, dim);
            entityBox.SetFromCuboidf(entity.CollisionBox, convPos);
            int x2 = (int) entityBox.X2;
            int y2 = (int) entityBox.Y2;
            int z2 = (int) entityBox.Z2;
            int z1 = (int) entityBox.Z1;
            BlockPos tmpPos = PhysicsBehaviorBaseVehicle.vCollisionTester.tmpPos;
            tmpPos.SetDimension(entity.Pos.Dimension);
            for (int y1 = (int) entityBox.Y1; y1 <= y2; ++y1)
            {
                for (int x1 = (int) entityBox.X1; x1 <= x2; ++x1)
                {
                    for (int z = z1; z <= z2; ++z)
                    {
                        tmpPos.Set(x1, y1, z);
                        blockAccessor.GetBlock(tmpPos).OnEntityInside(entity.World, entity, tmpPos);
                    }
                }
            }
            Action<float> onPhysicsTickCallback = this.OnPhysicsTickCallback;
            if (onPhysicsTickCallback != null)
            {
                onPhysicsTickCallback(0f);
            }
            PhysicsTickDelegate physicsUpdateWatcher = entity.PhysicsUpdateWatcher;
            if (physicsUpdateWatcher == null)
            {
                return;
            }
            physicsUpdateWatcher(0f, this.prevPos);
        }
        
    }
    
    public Action<float> OnPhysicsTickCallback2;
  
    private bool Matches(Entity t1)
    {
        return ((t1.WatchedAttributes.GetAttribute("dim") as IntAttribute)?.value != null);
    }

    public override void AfterPhysicsTick(float dt)
    {
        Action afterPhysicsTick = this.Entity.AfterPhysicsTick;
        if (afterPhysicsTick == null)
            return;
        afterPhysicsTick();
    }

    protected virtual bool IsFirstTick(Entity entity)
    {
        EntityPos previousServerPos = entity.PreviousServerPos;
        return previousServerPos.X == 0.0 && previousServerPos.Y == 0.0 && previousServerPos.Z == 0.0 && this.prevPos.X == 0.0 && this.prevPos.Y == 0.0 && this.prevPos.Z == 0.0;
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (this.sapi == null)
            return;
        this.sapi.Server.RemovePhysicsTickable((IPhysicsTickable) this);
    }

    public EntityPos GetConvertedPos(EntityPos vehiclePos, EntityPos entityPos, int subDimensionId)
    {
        EntityPos output = new EntityPos();
        output = (VehicleCollisionTester.FindRelativeEntityPosition(vehiclePos, entityPos)).Copy();
        output.X += (int)(subDimensionId % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
        output.Y += 8192 /*0x2000*/;
        output.Z += (int)(subDimensionId / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
        double[] vehicleRotation = PsuedoCuboidd.ConvertEulerAngles(vehiclePos.Pitch, vehiclePos.Yaw, vehiclePos.Roll);
        float[] eulerAngles = Quaterniond.ToEulerAngles([-vehicleRotation[0], -vehicleRotation[1] , -vehicleRotation[2] , vehicleRotation[3]]);
        output.Pitch = eulerAngles[0];
        output.Yaw = eulerAngles[1];
        output.Roll = eulerAngles[2];
        return output;
    }

    public Dictionary<int, EntityVehicle> GetNearbyVehicles(Entity entity)
    {
        //EntityVehicle[] EntityVehicleList = (EntityVehicle[])entity.Api.World.GetEntitiesAround(
        //  entity.Pos.XYZ,
        //  (float)entity.Api.World.DefaultEntityTrackingRange, (float)entity.Api.World.DefaultEntityTrackingRange,
        //  Matches);
        //return EntityVehicleList;
        return GetNearbyVehicles(entity.Pos, entity.Api.World);
    }
  
    public Dictionary<int, EntityVehicle> GetNearbyVehicles(EntityPos entityPos, IWorldAccessor world)
    {
        if (this.Entity == null) return new Dictionary<int, EntityVehicle>();
        //this.Entity.Api.Logger.Event("Getting Nearby Vehicles");
        Dictionary<int, EntityVehicle> copy = nearbyVehiclesList;
        Entity[] entityList = world.GetEntitiesAround(
            entityPos.XYZ,
            (float)world.DefaultEntityTrackingRange, (float)world.DefaultEntityTrackingRange,
            Matches);
        for (int i = 0; i < entityList.Length; i++)
        {
            int? tmpDim = ((entityList[i].WatchedAttributes.GetAttribute("dim") as IntAttribute).value);
            if (tmpDim == null) continue;
            //this.Entity.Api.Logger.Event("tmpDim: " + tmpDim.Value);

            if (nearbyVehiclesList.ContainsKey(tmpDim.Value)) continue;
      
            EntityVehicle tmpChunky = new EntityVehicle();
            tmpChunky.Stats = entityList[i].Stats;
            tmpChunky.WatchedAttributes.SetInt("dim", tmpDim.Value);
            this.nearbyVehiclesList.Add((tmpChunky.WatchedAttributes.GetAttribute("dim") as IntAttribute).value, tmpChunky);
        }

        /*
        foreach (int key in nearbyVehiclesList.Keys)
        {
          //remove the vehicles that are still nearby
          copy.Remove(key);
        }
        foreach (int key in copy.Keys)
        {
          //remove the vehicles that are no longer nearby
          nearbyVehiclesList.Remove(key);
        }
        */
        for (int i = 0; i < nearbyVehiclesList.Count; i++)
        {
            vehiclePosList = new EntityPos[nearbyVehiclesList.Count];
            vehiclePosList[i] = nearbyVehiclesList.Values.ElementAt(i).Pos;
        }
        return nearbyVehiclesList;
    }

    public void AddVehicle(long entityId, int subDimensionId)
    {
        bool success = nearbyVehiclesList.TryAdd(subDimensionId, (EntityVehicle)this.entity.World.GetEntityById(entityId));
        this.subDimensionIdList = nearbyVehiclesList.Keys.ToArray();
        //if (success) capi.Logger.Event("Success!");
        //else if () capi.Logger.Event("Failed!");
    }
  

    public void Dispose()
    {
    }

    // Do physics every frame on the client.
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (capi.IsGamePaused) return;

        // Unregister the entity if it isn't the player.
        if (capi.World.Player.Entity != entity)
        {
            smoothStepping = false;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            return;
        }

        accum += dt;

        if (accum > 0.5f)
        {
            accum = 0.5f;
        }

        var mountedEntity = entityPlayer.MountedOn?.Entity;
        IPhysicsTickable tickable = null;
        if (entityPlayer.MountedOn?.MountSupplier.Controller == entityPlayer)
        {
            tickable = mountedEntity?.SidedProperties.Behaviors.Find(b => b is IPhysicsTickable) as IPhysicsTickable;
        }

        while (accum >= interval)
        {
            OnPhysicsTick(interval);
            tickable?.OnPhysicsTick(interval);

            accum -= interval;
            currentTick++;

            // Send position every 4 ticks.
            if (currentTick % 4 == 0)
            {
                if (entityPlayer.EntityId != 0 && entityPlayer.Alive)
                {
                    capi.Network.SendPlayerPositionPacket();
                    if (tickable != null)
                    {
                        capi.Network.SendPlayerMountPositionPacket(mountedEntity);
                    }
                }
            }

            AfterPhysicsTick(interval);
            tickable?.AfterPhysicsTick(interval);
        }

        // For camera, lerps from prevPos to current pos by 1 + accum.
        entity.PhysicsUpdateWatcher?.Invoke(accum, prevPos);
        mountedEntity?.PhysicsUpdateWatcher?.Invoke(accum, prevPos);
    }

    public double RenderOrder { get; }
    public int RenderRange { get; }
}