
using System.Collections.Generic;
using System.Numerics;
using HarmonyLib;
using PhysicsLib.Entities.Behaviours;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using PhysicsLib.Api;
using PhysicsLib.Api.CollisionSource;
using PhysicsLib.Client;
using PhysicsLib.patches;
using BlockyVehicleLib.Entities;
using PhysicsLib;
using PhysicsLib.Entities.Behaviours;
using Vintagestory.API.Client;
using Vintagestory.API.Common;


namespace BlockyVehicleLib.Patches
{
    
    
    [HarmonyPatch(typeof(DynamicPhysicsBehaviour), nameof(DynamicPhysicsBehaviour.HandleEntityChunky))]
    public static class DynamicPhysicsBehavior_Patch//This may not even do anything
    {
        [HarmonyPrefix]
        public static bool Prefix(Entity entity, out BuiltCompound? cachedShape)
        {
            entity.Api.Logger.Event("Prefix EntityChunky executing");
            PhysicsLibModSystem physics = entity.Api.ModLoader.GetModSystem<PhysicsLibModSystem>();
            cachedShape = null;
            if (entity is EntityVehicle)
            {
                
                if (((EntityVehicle)entity).spawned)
                {
                    cachedShape = ((EntityVehicle)entity).GetShape();
                    if (cachedShape == null)
                    {
                        entity.Api.Logger.Event("cachedShape is null");
                        return false;
                    }
                    if(cachedShape.Boxes.Count == 0) entity.Api.Logger.Event("cachedShape.ManualChildBoxes is empty");
                    entity.Api.Logger.Event("cachedShape.LocalCenterOfMassOffset: " + cachedShape.LocalCenterOfMassOffset);
                }
            }
            else
            {
                CompositeShape shape = Block.DefaultCubeShape;
                AssetLocation shapeLoc = shape.Base.Clone();
                shapeLoc.Path = "shapes/" + shapeLoc.Path + ".json";
                
                cachedShape = physics.TryGetCompoundShape(shapeLoc.Path);
            }
            return false;
            //bypass the json step entirely by manually constructing the BuiltCompound
        }
    }
}