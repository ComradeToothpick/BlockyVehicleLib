using System;
using Vintagestory.API;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using BlockyVehicleLib.Entities;
using BlockyVehicleLib.Network;

#nullable disable

namespace BlockyVehicleLib.Entities;

/// <summary>
/// This provides functionality for physics-based entity behaviors. It is not an entity behavior on its own.
/// </summary>
[DocumentAsJson]
public abstract class PhysicsBehaviorBaseVehicle : EntityBehavior
{
    protected ICoreClientAPI capi;
    protected ICoreServerAPI sapi;
    protected Vec3d nPos;
    protected EntityPos[] vehiclePosList;
    protected int[] subDimensionIdList;
    //private BlockyVehicleLibModSystem modSystem;

    // How often the client should be sending updates.
    protected const float clientInterval = 1 / 15f;

    protected int previousVersion;

    public IMountable mountableSupplier;

    protected readonly EntityPos lPos = new();

    public float CollisionYExtra = 1f;

    [ThreadStatic]
    protected static CachingVehicleCollisionTester vCollisionTester;

    public PhysicsBehaviorBaseVehicle(Entity entity)
    : base(entity)
    {
    }

    public virtual void Init() => Initialize();
    
    public virtual void Initialize()
    {
        if (entity.Api is ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        if (entity.Api is ICoreServerAPI sapi)
        {
            this.sapi = sapi;
        }
    }
    
    public static void InitServerMT(ICoreServerAPI sapi, int dummy)
    {
        PhysicsBehaviorBaseVehicle.vCollisionTester = new CachingVehicleCollisionTester();
        sapi.Event.PhysicsThreadStart += (Action) (() => PhysicsBehaviorBaseVehicle.vCollisionTester = new CachingVehicleCollisionTester());
        sapi.Logger.Event("PhysicsBehaviorBaseVehicle.InitServerMT executed successfully");
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        mountableSupplier = entity.GetInterface<IMountable>();
    }
}