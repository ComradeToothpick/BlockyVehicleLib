using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace BlockyVehicleLib.Entities;

public class BehaviorKinematicVehicleCollider : EntityBehaviorKinematicCollider
{
    public BehaviorKinematicVehicleCollider(Entity entity) : base(entity) {}
    
    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        disposed = false;
        if (((EntityVehicle)entity).CollisionBoxes == null || ((EntityVehicle)entity).CollisionBoxes.Count == 0)
        {
            throw new InvalidOperationException($"Entity {entity.Code} cannot define kinematic collision boxes, null or 0");
        }

        CollisionBoxes = ((EntityVehicle)entity).CollisionBoxes.ToArray();
        
        if (CollisionBoxes == null)
        {
            throw new InvalidOperationException($"Entity {entity.Code} cannot define kinematic collision boxes, null or 0");
        }
        else
        {
            if (CollisionBoxes.Length == 0) throw new InvalidOperationException($"Entity {entity.Code} requires at least one kinematic collision box.");

            localCollisionBoxes = new Cuboidd[CollisionBoxes.Length];
            for (int i = 0; i < CollisionBoxes.Length; i++)
            {
                localCollisionBoxes[i] = CollisionBoxes[i].ToDouble();
            }
        }

        CollisionLayer = attributes["collisionLayer"].AsObject(CollisionLayer);
        CollisionMask = attributes["collisionMask"].AsObject(CollisionMask);
        CarryEntities = attributes["carryEntities"].AsBool(CarryEntities);
        KeepsSupportedEntitiesDry = attributes["keepsSupportedEntitiesDry"].AsBool(KeepsSupportedEntitiesDry);
        RotateWithEntity = attributes["rotateWithEntity"].AsBool(RotateWithEntity);
        ColliderCode = attributes["colliderCode"].AsString(ColliderCode);
        if (string.IsNullOrEmpty(ColliderCode)) throw new InvalidOperationException($"Entity {entity.Code} requires a non-empty kinematic collider code.");

        if (entity.Api is ICoreClientAPI clientApi)
        {
            capi = clientApi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "kinematiccollider");
        }
    }
}