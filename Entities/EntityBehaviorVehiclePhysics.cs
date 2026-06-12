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

[DocumentAsJson]
[AddDocumentationProperty("waterDragFactor", "Gravity drag factor when in water", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("airDragFactor", "Gravity drag factor when falling. Overrides airDragFallingFactor when present", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("airDragFallingFactor", "Gravity drag factor when falling", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("groundDragFactor", "Horizontal drag factor when on the ground", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("gravityFactor", "Multiplier for gravity strength", "System.Double", "Optional", "1", false)]
public class EntityBehaviorVehiclePhysics(Entity entity) : 
    EntityControlledVehiclePhysics(entity),
    IPhysicsTickable,
    IRemotePhysics,
    IRenderer,
    IDisposable
{
    protected readonly Vec3d prevPos = new Vec3d();
    protected double motionBeforeY;
    protected bool feetInLiquidBefore;
    protected bool onGroundBefore;
    protected bool swimmingBefore;
    protected bool collidedBefore;
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
        this.onGroundBefore = entity.OnGround;
        this.feetInLiquidBefore = entity.FeetInLiquid;
        this.swimmingBefore = entity.Swimming;
        this.collidedBefore = entity.Collided;
    }

    public virtual void SetProperties(JsonObject attributes)
    {
        //Not using this yet, will likely have vehicle specific config settings later
    
        //this.WaterDragValue = 1.0 - (1.0 - this.WaterDragValue) * attributes["waterDragFactor"].AsDouble(1.0);
        //JsonObject attribute = (JsonObject)attributes["airDragFactor"];
        //this.AirDragValue = 1.0 - (1.0 - this.AirDragValue) * (attribute.Exists ? attribute.AsDouble(1.0) : attributes["airDragFallingFactor"].AsDouble(1.0));
        //if (this.entity.WatchedAttributes.HasAttribute("airDragFactor"))
        //  this.AirDragValue = 1.0 - (1.0 - (double) GlobalConstants.AirDragAlways) * this.entity.WatchedAttributes.GetDouble("airDragFactor", 0.0);
        //this.GroundDragValue = 0.3 * attributes["groundDragFactor"].AsDouble(1.0);
        //this.GravityPerSecond *= attributes["gravityFactor"].AsDouble(1.0);
        //this.BoyancyMul = attributes["boyancyMul"].AsDouble(1.0);
        //if (!this.entity.WatchedAttributes.HasAttribute("gravityFactor"))
        //  return;
        //this.GravityPerSecond = (double) GlobalConstants.GravityPerSecond * this.entity.WatchedAttributes.GetDouble("gravityFactor", 0.0);
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize();
        vCollisionTester = new CachingVehicleCollisionTester();//If this works, I don't need a harmony patch
        this.SetProperties(attributes);
    
        if (this.Entity.Api is ICoreServerAPI api)
        {
            sapi.Logger.Event("EntityBehaviorVehiclePhysics Initializing");
            api.Server.AddPhysicsTickable((IPhysicsTickable) this);
        }
        else
        {
            EnumHandling handled = EnumHandling.Handled;
            this.OnReceivedServerPos(true, ref handled);
        }
    }

    public override void OnReceivedClientPos(int version)
    {
        if (version > this.previousVersion)
        {
            this.previousVersion = version;
            this.HandleRemotePhysics(0.06666667f, true);
        }
        else
            this.HandleRemotePhysics(0.06666667f, false);
    }

  
    //This probably needs to be updated to apply tests for each relativePos for each (nearby) vehicle
    public override void HandleRemotePhysics(float dt, bool isTeleport)
    {
        if (this.nPos == (Vec3d) null)
        {
            this.nPos = new Vec3d();
            this.nPos.Set(this.Entity.Pos);
        }
    
        float dtFactor = dt * 60f;
        EntityPos lPos = this.lPos;
        lPos.SetFrom(this.nPos);
        this.nPos.Set(this.entity.Pos);
        Vec3d motion = lPos.Motion;
        if (isTeleport)
            lPos.SetFrom(this.nPos);
        //motion.X = (this.nPos.X - lPos.X) / (double) dtFactor;
        //motion.Y = (this.nPos.Y - lPos.Y) / (double) dtFactor;
        //motion.Z = (this.nPos.Z - lPos.Z) / (double) dtFactor;
        if (motion.Length() > 20.0)
            motion.Set(0.0, 0.0, 0.0);
        //this.entity.Pos.Motion.Set(motion);
        PhysicsBehaviorBaseVehicle.vCollisionTester.NewTick(lPos);
        this.SetState(lPos);
        this.RemoteMotionAndCollision(lPos, dtFactor);
        this.ApplyTests(lPos);
    }

    public void RemoteMotionAndCollision(EntityPos pos, float dtFactor)
    {
        //removed the gravity from this
        if (vehiclesNearby) PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(this.Entity, pos, this.vehiclePosList/*EntityVehiclePosList*/, dtFactor, ref this.newPos, this.subDimensionIdList/*subDimensionId*/, 0.0f, this.CollisionYExtra);
        //else PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(this.Entity, pos, dtFactor, ref this.newPos, 0.0f, this.CollisionYExtra);
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
            if (this.Entity == null) sapi.Logger.Error("Entity is null");
            if (pos == null) sapi.Logger.Error("pos is null");
            if (this.vehiclePosList == null) sapi.Logger.Error("vehiclePosList is null");
            if (dtFactor == null) sapi.Logger.Error("dtFactor is null");
            if (this.newPos == null) sapi.Logger.Error("newPos is null");
            if (this.subDimensionIdList == null) sapi.Logger.Error("subDimensionIdList is null");//this one should be fixed?
            if (this.CollisionYExtra == null) sapi.Logger.Error("CollisionYExtra is null");
            if (vCollisionTester == null)  sapi.Logger.Error("vCollisionTester is null");
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
        //Removed redundant physics
        {
            PsuedoCuboidd entityBox = new PsuedoCuboidd();
      
            foreach (int key in nearbyVehiclesList.Keys)
            {
                int dim = key;
                EntityPos convPos = GetConvertedPos(nearbyVehiclesList.TryGetValue(key).Pos, entity.Pos, dim);
                entityBox.SetFromCuboidf(entity.SelectionBox, convPos);
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
            }
        }
    }

    public override void OnPhysicsTick(float dt)
    {
        Entity entity = this.Entity;
        if (entity.State != EnumEntityState.Active || !this.Ticking)
        {
            return;
        }
        if (entity.Api.Side == EnumAppSide.Server)
        {
            if (tickCounter++ > 20)
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

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        throw new NotImplementedException();
    }

    public double RenderOrder { get; }
    public int RenderRange { get; }
}