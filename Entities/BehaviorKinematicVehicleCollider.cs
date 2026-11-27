using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BlockyVehicleLib.Entities;

public class BehaviorKinematicVehicleCollider(Entity entity) : 
    EntityBehavior(entity),
    IRenderer,
    IDisposable,
    IEntityColliderProvider
{
    public const string DefaultColliderCode = "vehicle";
    //public CompositeShape? CollisionShape;
    public Cuboidf[] CollisionBoxes;
    [DocumentAsJson("Optional", "true", false)]
    public bool CarryEntities = true;
    [DocumentAsJson("Optional", "false", false)]
    public bool KeepsSupportedEntitiesDry;
    [DocumentAsJson("Optional", "true", false)]
    public bool RotateWithEntity = true;
    [DocumentAsJson("Optional", "main", false)]
    public string ColliderCode = "main";
    [DocumentAsJson("Optional", "Solid", false)]
    public CollisionLayer CollisionLayer = CollisionLayer.Solid;
    [DocumentAsJson("Optional", "All", false)]
    public CollisionLayer CollisionMask = CollisionLayer.All;
    public int colliderDimension;
    private readonly double[] rotation = Quaterniond.Create();
    private long entityId;
    public Cuboidd[] localCollisionBoxes;
    public IKinematicColliderTransformProvider? transformProvider;
    public ICoreClientAPI? capi;
    public ColliderTransform transform;
    public long colliderId;
    public bool hasTransform;
    public bool wasMoving;
    public bool disposed;
    private int itteration = 0;
    private JsonObject attr;

    public BehaviorKinematicVehicleCollider(Entity entity, Cuboidd[] collisionBoxes, Cuboidf[] collisionFBoxes) : this(entity)
    {
        this.localCollisionBoxes = collisionBoxes;
        this.CollisionBoxes = collisionFBoxes;
    }
    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        attr = attributes;
        disposed = false;
        //CollisionShape = attributes["collisionShape"].AsObject<CompositeShape>((CompositeShape) null, entity.Code.Domain);
        Cuboidf[] cuboidfArray = attributes["collisionBoxes"].AsArray<Cuboidf>();
        entityId = entity.EntityId;
        if (cuboidfArray == null && (CollisionBoxes == null || CollisionBoxes.Length == 0))
        {
            CollisionBoxes = new Cuboidf[1]
            {
                entity.CollisionBox.Clone()
            };
            localCollisionBoxes = new Cuboidd[1]
            {
                CollisionBoxes[0].ToDouble()
            };
        }
        else if (CollisionBoxes == null)
        {
            CollisionBoxes = cuboidfArray.Length != 0 ? new Cuboidf[cuboidfArray.Length] : throw new InvalidOperationException($"Entity {entity.Code} requires at least one kinematic collision box.");
            for (int index1 = 0; index1 < cuboidfArray.Length; ++index1)
            {
                Cuboidf[] collisionBoxes = CollisionBoxes;
                int index2 = index1;
                collisionBoxes[index2] = cuboidfArray[index1] ?? throw new InvalidOperationException($"Entity {this.entity.Code} has a null kinematic collision box.");
            }
            localCollisionBoxes = new Cuboidd[CollisionBoxes.Length];
            for (int index = 0; index < CollisionBoxes.Length; ++index)
                localCollisionBoxes[index] = CollisionBoxes[index].ToDouble();
        }
        CollisionLayer = attributes["collisionLayer"].AsObject(CollisionLayer);
        CollisionMask = attributes["collisionMask"].AsObject(CollisionMask);
        CarryEntities = attributes["carryEntities"].AsBool(CarryEntities);
        KeepsSupportedEntitiesDry = attributes["keepsSupportedEntitiesDry"].AsBool(KeepsSupportedEntitiesDry);
        RotateWithEntity = attributes["rotateWithEntity"].AsBool(RotateWithEntity);
        ColliderCode = DefaultColliderCode + "_" + entity.EntityId;
        if (string.IsNullOrEmpty(ColliderCode)) throw new InvalidOperationException($"Entity {entity.Code} requires a non-empty kinematic collider code.");
        entity.Api.Logger.Event("BehaviorKinematicVehicleCollider initialized!");
        if (entity.Api is ICoreClientAPI clientApi)
        {
            capi = clientApi;
            capi.Logger.Event("BehaviorKinematicVehicleCollider Client API Initialized!");
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "vehiclekinematiccollider");
        }
    }
    
    public override void AfterInitialized(bool onFirstSpawn)
    {
        if (RotateWithEntity)
            transformProvider = entity.GetInterface<IKinematicColliderTransformProvider>();
        UpdateCollider(0.0f);
    }

    public override string PropertyName() => "vehiclekinematiccollider";
    
    public override void OnEntityDespawn(EntityDespawnData despawn) => Dispose();

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        if (colliderId == 0L)
            return;
        entity.World.CollisionScene.UnregisterCollider(colliderId);
        colliderId = 0L;
        hasTransform = false;
        wasMoving = false;
    }
    
    public long ColliderId => colliderId;

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server)
            return;
        UpdateCollider(deltaTime);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        ICoreClientAPI capi = this.capi;
        if ((capi != null ? (capi.IsGamePaused ? 1 : 0) : 0) != 0)
            return;
        EntityPlayer entity = this.capi?.World.Player.Entity;
        if (entity?.MountedOn?.Entity == this.entity && entity.MountedOn.MountSupplier.Controller == entity)
            return;
        
        UpdateCollider(deltaTime);
    }
    
    public void UpdateCollider(float elapsedSeconds)
    {
        if (disposed)
            return;
        ColliderTransform transform = getTransform();
        bool flag = hasTransform && !transform.Equals(this.transform);
        if (hasTransform && !flag && !wasMoving && colliderDimension == entity.Pos.Dimension)
            return;
        colliderDimension = entity.Pos.Dimension;
        this.transform = transform;
        hasTransform = true;
        if (colliderId == 0L)
        {
            colliderId = entity.World.CollisionScene.RegisterTransformedCollider(
                entity.Pos.Dimension,
                localCollisionBoxes,
                this.transform,
                CarryEntities,
                entityId,
                ColliderCode,
                KeepsSupportedEntitiesDry,
                CollisionLayer,
                CollisionMask);
            wasMoving = false;
        }
        else
        {
            entity.World.CollisionScene.UpdateColliderTransform(colliderId, entity.Pos.Dimension, this.transform, elapsedSeconds);
            wasMoving = flag;
        }
    }

    public void SetCollisionBoxes(Cuboidd[] collisionBoxes, int[] blockIds)
    {
        entity.Api.Logger.Event("BehaviorKinematicVehicleCollider SetCollisionBoxes Called!");
        entity.World.CollisionScene.UnregisterEntityColliders(entity.EntityId);
        if (localCollisionBoxes == null)
        {
            localCollisionBoxes = new Cuboidd[collisionBoxes.Length];
            
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                if (collisionBoxes[i] == null) collisionBoxes[i] = new Cuboidd(0, 0, 0, 0, 0, 0);
                localCollisionBoxes[i] = collisionBoxes[i].Clone();
            }
        }

        for (int i = 0; i < collisionBoxes.Length; i++)
        {
            entity.Api.Logger.Event("collisionBox[" + i + "] = " + collisionBoxes[i].X1 + ", " + collisionBoxes[i].Y1 + ", " + collisionBoxes[i].Z1 + ", " + collisionBoxes[i].X2 + ", " + collisionBoxes[i].Y2 + ", " + collisionBoxes[i].Z2);
        }
        ColliderCode = DefaultColliderCode + "_" + entity.EntityId;
        colliderId = entity.World.CollisionScene.RegisterTransformedCollider(
            entity.Pos.Dimension,
            collisionBoxes.ToList(),
            getTransform(),
            CarryEntities,
            entity.EntityId,
            ColliderCode,
            KeepsSupportedEntitiesDry,
            CollisionLayer,
            CollisionMask);
        entity.Api.Logger.Event("Collider registered! " + colliderId);
        entity.World.CollisionScene.TryGetCollider(entity.EntityId, ColliderCode, out Collider? collider);
        if (collider != null)
        {
            entity.Api.Logger.Event("Collider found! " + collider.Id + ". CollisionBoxCount: " + collider.CollisionBoxCount);
            for (int j = 0; j < collider.CollisionBoxCount; j++)
            {
                Cuboidd? collisionBox = collider.GetCollisionBox(j);
                entity.Api.Logger.Event("Collision Box " + j + ": " + collisionBox.MinX + ", " + collisionBox.MinY + ", " + collisionBox.MinZ + ", " + collisionBox.MaxX + ", " + collisionBox.MaxY + ", " + collisionBox.MaxZ);
            }
            entity.Api.Logger.Event("Collision Box Bounds: " + collider.Bounds.MinX + ", " + collider.Bounds.MinY + ", " + collider.Bounds.MinZ + ", " + collider.Bounds.MaxX + ", " + collider.Bounds.MaxY + ", " + collider.Bounds.MaxZ);
        }
        
        CollisionLayer = attr["collisionLayer"].AsObject(CollisionLayer);
        CollisionMask = attr["collisionMask"].AsObject(CollisionMask);
        CarryEntities = attr["carryEntities"].AsBool(CarryEntities);
        KeepsSupportedEntitiesDry = attr["keepsSupportedEntitiesDry"].AsBool(KeepsSupportedEntitiesDry);
        RotateWithEntity = attr["rotateWithEntity"].AsBool(RotateWithEntity);
        if (string.IsNullOrEmpty(ColliderCode)) throw new InvalidOperationException($"Entity {entity.Code} requires a non-empty kinematic collider code.");
        
        if (entity.Api is ICoreClientAPI clientApi)
        {
            capi = clientApi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "vehiclekinematiccollider");
        }
        UpdateCollider(0);
    }
    
    public ColliderTransform getTransform()
    {
        EntityPos pos = entity.Pos;
        if (!RotateWithEntity)
            return new ColliderTransform(pos);
        if (transformProvider != null)
            return transformProvider.GetKinematicColliderTransform();
        rotation[0] = 0.0;
        rotation[1] = 0.0;
        rotation[2] = 0.0;
        rotation[3] = 1.0;
        Quaterniond.RotateX(rotation, rotation, pos.Pitch);
        Quaterniond.RotateY(rotation, rotation, pos.Yaw);
        Quaterniond.RotateZ(rotation, rotation, pos.Roll);
        return new ColliderTransform(pos, rotation);
    }

    public double RenderOrder { get; } = 0.01;
    public int RenderRange { get; } = 9999;
    public bool TryGetColliderId(string colliderCode, out long id)
    {
        if (colliderId != 0L && colliderCode == ColliderCode)
        {
            id = colliderId;
            return true;
        }
        id = 0L;
        return false;
    }
}