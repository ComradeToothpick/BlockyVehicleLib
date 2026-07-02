
using System.Collections.Generic;
using System.Numerics;
using HarmonyLib;
using PhysicsLib.Entities.Behaviours;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using PhysicsLib.Api;
using PhysicsLib.Client;
using PhysicsLib.patches;
using BlockyVehicleLib.Entities;
using PhysicsLib;
using PhysicsLib.Entities.Behaviours;
using Vintagestory.API.Client;
using Vintagestory.API.Common;


namespace BlockyVehicleLib.Patches
{
    [HarmonyPatch(typeof(DynamicPhysicsBehaviour), nameof(DynamicPhysicsBehaviour.LoadCollider))]
    public static class DynamicPhysicsBehavior_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(DynamicPhysicsBehaviour __instance)
        {
            Entity entity = __instance.entity;
            if (entity is EntityVehicle)
            {
                entity.Api.Logger.Event("Prefix EntityChunky executing");
                PhysicsLibModSystem physics = entity.Api.ModLoader.GetModSystem<PhysicsLibModSystem>();
                CompoundCollider cachedShape = null;
                if (((EntityVehicle)entity).spawned)
                {
                    cachedShape = ((EntityVehicle)entity).GetShape();
                    if (cachedShape == null)
                    {
                        entity.Api.Logger.Event("cachedShape is null");
                        return false;
                    }
                    if(cachedShape.Boxes.Length == 0) entity.Api.Logger.Event("cachedShape.ManualChildBoxes is empty");
                    entity.Api.Logger.Event("cachedShape.LocalCenterOfMassOffset: " + cachedShape.LocalCenterOfMassOffset);
                }
                return false;
            }
            return true;
        }
    }
}