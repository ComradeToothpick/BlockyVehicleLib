using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace BlockyVehicleLib.Entities;

public class BehaviorPassivePhysicsVehicle : EntityBehaviorPassivePhysics 
{
    public BehaviorPassivePhysicsVehicle(Entity entity) : base(entity)
    {
        //mcollisionTester ??= new MultiCollisionTester();   // Required on clientside
    }
    
    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        //CollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
        //OrigCollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
        base.Initialize(properties, attributes);
    }
    
    public override void SetProperties(JsonObject attributes)
    {
        base.SetProperties(attributes);

        //CollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
        //OrigCollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
    }

    public void UpdateCollisionBoxes(Entity entity)
    {
        //CollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
        //OrigCollisionBoxes = ((EntityVehicle)entity).OrigCollisionBox.ToArray();
    }
}