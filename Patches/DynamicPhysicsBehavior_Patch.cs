
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
        public static bool Prefix(DynamicPhysicsBehaviour __instance, CompoundCollider? __result)
        {
            if (__instance.entity is EntityVehicle)
            {
                __result = null;
                return false;
            }
            return true;
        }
    }
}