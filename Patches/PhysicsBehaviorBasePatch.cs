using BlockyVehicleLib.Entities;
using HarmonyLib;
using Vintagestory.API.Server;

namespace BlockyVehicleLib.Patches;

[HarmonyPatch(typeof(Vintagestory.API.Common.Entities.PhysicsBehaviorBase), "InitServerMT")]
public class PhysicsBehaviorBasePatch
{
    static void Postfix(ICoreServerAPI sapi)
    {
        PhysicsBehaviorBaseVehicle.InitServerMT(sapi, 1);
    }
}