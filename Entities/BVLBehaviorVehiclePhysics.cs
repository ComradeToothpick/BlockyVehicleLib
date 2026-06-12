using System;
using Vintagestory.API;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;


namespace BlockyVehicleLib.Entities;

[DocumentAsJson]
[AddDocumentationProperty("waterDragFactor", "Gravity drag factor when in water", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("airDragFactor", "Gravity drag factor when falling. Overrides airDragFallingFactor when present", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("airDragFallingFactor", "Gravity drag factor when falling", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("groundDragFactor", "Horizontal drag factor when on the ground", "System.Double", "Optional", "1", false)]
[AddDocumentationProperty("gravityFactor", "Multiplier for gravity strength", "System.Double", "Optional", "1", false)]
public class VehicleBehaviourVehiclePhysics(EntityChunky entity) : 
    PhysicsBehaviorBaseVehicle(entity),
    IPhysicsTickable,
    IRemotePhysics
{
  protected readonly Vec3d prevPos = new Vec3d();
  protected double motionBeforeY;
  protected bool feetInLiquidBefore;
  protected bool onGroundBefore;
  protected bool swimmingBefore;
  protected bool collidedBefore;
  protected Vec3d newPos = new Vec3d();
  /// <summary>The amount of drag while travelling through water.</summary>
  public double WaterDragValue = (double) GlobalConstants.WaterDrag;
  /// <summary>The amount of drag while travelling through the air.</summary>
  public double AirDragValue = (double) GlobalConstants.AirDragAlways;
  /// <summary>The amount of drag while travelling on the ground.</summary>
  public double GroundDragValue = 0.699999988079071;
  /// <summary>The amount of drag while travelling on the ground.</summary>
  public double BoyancyMul = 1.0;
  /// <summary>
  /// The amount of gravity applied per tick to this entity.
  /// </summary>
  public double GravityPerSecond = (double) GlobalConstants.GravityPerSecond;
  /// <summary>
  /// If set, will test for entity collision every tick (expensive)
  /// </summary>
  public Action<float> OnPhysicsTickCallback;
  [ThreadStatic]
  private static BlockPos tmpPos;

  public Vintagestory.API.Common.Entities.Entity Entity => this.entity;

  public bool Ticking { get; set; } = true;

  public void SetState(EntityPos pos)
  {
    this.prevPos.Set(pos);
    this.motionBeforeY = pos.Motion.Y;
    Entity entity = this.entity;
    this.onGroundBefore = entity.OnGround;
    this.feetInLiquidBefore = entity.FeetInLiquid;
    this.swimmingBefore = entity.Swimming;
    this.collidedBefore = entity.Collided;
  }

  public virtual void SetProperties(JsonObject attributes)
  {
    this.WaterDragValue = 1.0 - (1.0 - this.WaterDragValue) * attributes["waterDragFactor"].AsDouble(1.0);
    JsonObject attribute = attributes["airDragFactor"];
    this.AirDragValue = 1.0 - (1.0 - this.AirDragValue) * (attribute.Exists ? attribute.AsDouble(1.0) : attributes["airDragFallingFactor"].AsDouble(1.0));
    if (this.entity.WatchedAttributes.HasAttribute("airDragFactor"))
      this.AirDragValue = 1.0 - (1.0 - (double) GlobalConstants.AirDragAlways) * this.entity.WatchedAttributes.GetDouble("airDragFactor", 0.0);
    this.GroundDragValue = 0.3 * attributes["groundDragFactor"].AsDouble(1.0);
    this.GravityPerSecond *= attributes["gravityFactor"].AsDouble(1.0);
    this.BoyancyMul = attributes["boyancyMul"].AsDouble(1.0);
    if (!this.entity.WatchedAttributes.HasAttribute("gravityFactor"))
      return;
    this.GravityPerSecond = (double) GlobalConstants.GravityPerSecond * this.entity.WatchedAttributes.GetDouble("gravityFactor", 0.0);
  }

  public void Initialize(EntityProperties properties, JsonObject attributes)
  {
    base.Initialize();
    this.SetProperties(attributes);
    if (this.entity.Api is ICoreServerAPI api)
    {
      api.Server.AddPhysicsTickable((IPhysicsTickable) this);
    }
    else
    {
      EnumHandling handled = EnumHandling.Handled;
      this.OnReceivedServerPos(true, ref handled);
    }
  }

  public void OnReceivedClientPos(int version)
  {
    if (version > this.previousVersion)
    {
      this.previousVersion = version;
      this.HandleRemotePhysics(0.06666667f, true);
    }
    else
      this.HandleRemotePhysics(0.06666667f, false);
  }

  public void HandleRemotePhysics(float dt, bool isTeleport)
  {
    if (this.nPos == (Vec3d) null)
    {
      this.nPos = new Vec3d();
      this.nPos.Set(this.entity.Pos);
    }
    float dtFactor = dt * 60f;
    EntityPos lPos = this.lPos;
    lPos.SetFrom(this.nPos);
    this.nPos.Set(this.entity.Pos);
    Vec3d motion = lPos.Motion;
    if (isTeleport)
      lPos.SetFrom(this.nPos);
    motion.X = (this.nPos.X - lPos.X) / (double) dtFactor;
    motion.Y = (this.nPos.Y - lPos.Y) / (double) dtFactor;
    motion.Z = (this.nPos.Z - lPos.Z) / (double) dtFactor;
    if (motion.Length() > 20.0)
      motion.Set(0.0, 0.0, 0.0);
    this.entity.Pos.Motion.Set(motion);
    PhysicsBehaviorBaseVehicle.vCollisionTester.NewTick(lPos);
    this.SetState(lPos);
    this.RemoteMotionAndCollision(lPos, dtFactor);
    this.ApplyTests(lPos);
  }

  public void RemoteMotionAndCollision(EntityPos pos, float dtFactor)
  {
    double num = this.GravityPerSecond / 60.0 * (double) dtFactor + Math.Max(0.0, -0.014999999664723873 * pos.Motion.Y * (double) dtFactor);
    pos.Motion.Y -= num;
    PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(this.entity, pos, dtFactor, ref this.newPos, 0.0f, this.CollisionYExtra);
    this.entity.OnGround = this.entity.CollidedVertically & pos.Motion.Y < 0.0;
    pos.Motion.Y += num;
    pos.SetPos(this.nPos);
  }

  public void MotionAndCollision(EntityPos pos, float dt)
  {
    float dtFactor = 60f * dt;
    Entity entity = this.entity;
    Vec3d motion = pos.Motion;
    IBlockAccessor blockAccessor = entity.World.BlockAccessor;
    int dimension = pos.Dimension;
    if (this.onGroundBefore)
    {
      if (motion.HorLength() < 1E-05)
      {
        motion.X = 0.0;
        motion.Z = 0.0;
      }
      else if (!this.feetInLiquidBefore)
      {
        double num = 1.0 - this.GroundDragValue * (double) blockAccessor.GetBlockRaw((int) pos.X, (int) (pos.InternalY - 0.05000000074505806), (int) pos.Z, 1).DragMultiplier;
        motion.X *= num;
        motion.Z *= num;
      }
    }
    Block block = (Block) null;
    if (this.feetInLiquidBefore || this.swimmingBefore)
    {
      motion.Scale(Math.Pow(this.WaterDragValue, (double) dt * 33.0));
      if ((object) VehicleBehaviourVehiclePhysics.tmpPos == null)
        VehicleBehaviourVehiclePhysics.tmpPos = new BlockPos(pos.Dimension);
      VehicleBehaviourVehiclePhysics.tmpPos.Set(pos);
      block = blockAccessor.GetBlock(VehicleBehaviourVehiclePhysics.tmpPos, 2);
      if (this.feetInLiquidBefore && block is IBlockFlowing blockFlowing && !blockFlowing.IsStill)
      {
        float num = 300f / GameMath.Clamp((float)entity.MaterialDensity, 750f, 2500f) * dtFactor;
        FastVec3f pushVector = blockFlowing.GetPushVector(VehicleBehaviourVehiclePhysics.tmpPos);
        motion.Add(pushVector * num);
      }
    }
    else
      motion.Scale(Math.Pow(this.AirDragValue, (double) dt * 33.0));
    if (entity.ApplyGravity)
    {
      double num1 = this.GravityPerSecond / 60.0 * (double) dtFactor;
      if (entity.Swimming)
      {
        float num2 = GameMath.Clamp((float) (1.0 - (double) entity.MaterialDensity / (double) block.MaterialDensity), -1f, 1f);
        Block blockRaw = blockAccessor.GetBlockRaw((int) pos.X, (int) (pos.InternalY + 1.0), (int) pos.Z, 2);
        float num3 = GameMath.Clamp((float) ((double) (int) pos.Y + (double) block.LiquidLevel / 8.0 + (blockRaw.IsLiquid() ? 1.125 : 0.0) - pos.Y - ((double) entity.SelectionBox.Y2 - entity.SwimmingOffsetY)), 0.0f, 1f);
        double num4 = (double) GameMath.Clamp(60f * num2 * num3, -0.5f, 1.5f) - 1.0;
        double num5 = GameMath.Clamp(10.0 * Math.Abs(motion.Length() * (double) dtFactor) - 0.019999999552965164, 1.0, 1.25);
        motion.Y += num1 * num4 * this.BoyancyMul;
        motion.Mul(1.0 / num5);
      }
      else
        motion.Y -= num1;
    }
    double x = motion.X * (double) dtFactor + pos.X;
    double y = motion.Y * (double) dtFactor + pos.Y;
    double z = motion.Z * (double) dtFactor + pos.Z;
    this.applyCollision(pos, dtFactor);
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
  }

  protected virtual void applyCollision(EntityPos pos, float dtFactor)
  {
    PhysicsBehaviorBaseVehicle.vCollisionTester.ApplyTerrainCollision(this.entity, pos, dtFactor, ref this.newPos, 0.0f, this.CollisionYExtra);
  }

  public void ApplyTests(EntityPos pos)
  {
    Vintagestory.API.Common.Entities.Entity entity = this.entity;
    IBlockAccessor blockAccessor = entity.World.BlockAccessor;
    bool flag = pos.Motion.Y <= 0.0;
    entity.OnGround = entity.CollidedVertically & flag;
    Block blockRaw1 = blockAccessor.GetBlockRaw((int) pos.X, (int) pos.InternalY, (int) pos.Z, 2);
    entity.FeetInLiquid = blockRaw1.MatterState == EnumMatterState.Liquid;
    entity.InLava = blockRaw1.LiquidCode == "lava";
    if (entity.FeetInLiquid)
    {
      Block blockRaw2 = blockAccessor.GetBlockRaw((int) pos.X, (int) (pos.InternalY + 1.0), (int) pos.Z, 2);
      float num = (float) ((double) (int) pos.Y + (double) blockRaw1.LiquidLevel / 8.0 + (blockRaw2.IsLiquid() ? 1.125 : 0.0) - pos.Y - ((double) entity.SelectionBox.Y2 - entity.SwimmingOffsetY));
      entity.Swimming = (double) num > 0.0;
      if (!this.feetInLiquidBefore && (!(entity is EntityAgent entityAgent) || entityAgent.MountedOn == null) && !this.IsFirstTick(entity))
        entity.OnCollideWithLiquid();
    }
    else
    {
      entity.Swimming = false;
      if (this.swimmingBefore || this.feetInLiquidBefore)
        entity.OnExitedLiquid();
    }
    if (!this.collidedBefore && entity.Collided)
      entity.OnCollided();
    if (entity.OnGround)
    {
      if (!this.onGroundBefore)
        entity.OnFallToGround(this.motionBeforeY);
      entity.PositionBeforeFalling.Set(this.newPos);
    }
    if (GlobalConstants.OutsideWorld(pos.X, pos.Y, pos.Z, entity.World.BlockAccessor))
    {
      entity.DespawnReason = new EntityDespawnData()
      {
        Reason = EnumDespawnReason.Death,
        DamageSourceForDeath = new DamageSource()
        {
          Source = EnumDamageSource.Fall
        }
      };
    }
    else
    {
      Cuboidd entityBox = PhysicsBehaviorBaseVehicle.vCollisionTester.entityBox;
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
      Action<float> physicsTickCallback = this.OnPhysicsTickCallback;
      if (physicsTickCallback != null)
        physicsTickCallback(0.0f);
      PhysicsTickDelegate physicsUpdateWatcher = entity.PhysicsUpdateWatcher;
      if (physicsUpdateWatcher == null)
        return;
      physicsUpdateWatcher(0.0f, this.prevPos);
    }
  }

  public void OnPhysicsTick(float dt)
  {
    Entity entity = this.entity;
    if (entity.State != EnumEntityState.Active || !this.Ticking)
      return;
    IMountable mountableSupplier = this.mountableSupplier;
    if ((mountableSupplier != null ? (mountableSupplier.IsBeingControlled() ? 1 : 0) : 0) != 0 && entity.World.Side == EnumAppSide.Server)
      return;
    EntityPos pos = entity.Pos;
    PhysicsBehaviorBaseVehicle.vCollisionTester.AssignToEntity((PhysicsBehaviorBaseVehicle) this, pos.Dimension);
    int num = pos.Motion.Length() > 0.1 ? 10 : 1;
    float dt1 = dt / (float) num;
    for (int index = 0; index < num; ++index)
    {
      this.SetState(pos);
      this.MotionAndCollision(pos, dt1);
      this.ApplyTests(pos);
    }
    entity.Pos.SetFrom(pos);
  }

  public void AfterPhysicsTick(float dt)
  {
    Action afterPhysicsTick = this.entity.AfterPhysicsTick;
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

  public override string PropertyName() => "vehiclephysics";
}