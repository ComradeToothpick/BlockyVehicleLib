/*using System;
using System.Collections.Generic;
using BlockyVehicleLib.Entities;
using HarmonyLib;
using PhysicsLib.Api;
using PhysicsLib.Entities.Behaviours;
using PhysicsLib.patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace BlockyVehicleLib.Patches;
[HarmonyPatch(typeof(CollisionTester_ApplyTerrainCollision_Patch), nameof(CollisionTester_ApplyTerrainCollision_Patch.VehicleTerrainCollision))]
public class PhysicsLibPatchPatch
{
    private static readonly BlockPos collBlockPos;
    [HarmonyPostfix]
    public static void Postfix(
        CollisionTester instance,
        Entity entity,
        EntityPos entityPos,
        float dtFactor,
        ref Vec3d newPosition,
        float stepHeight = 1f,
        float yExtra = 1f)
    {
        if (!(entity is EntityVehicle)) return;
        
        EntityVehicle entityVehicle = entity as EntityVehicle;
        
        instance.minPos.SetDimension(entityPos.Dimension);

        List<LocalBox> boxList = entityVehicle.GetBehavior<DynamicPhysicsBehaviour>().VehicleChildBoxes;
        
        var worldAccessor = entity.World;
        Vec3d pos = instance.pos;           // Local copy for efficiency
        //Cuboidd entityBox = instance.entityBox; // Local copy for efficiency

        pos.X = entityPos.X;
        pos.Y = entityPos.Y;
        pos.Z = entityPos.Z;

        EnumPushDirection pushDirection = EnumPushDirection.None;

        entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);

        double motionX = entityPos.Motion.X * dtFactor;
        double motionY = entityPos.Motion.Y * dtFactor;
        double motionZ = entityPos.Motion.Z * dtFactor;

        // We need to make sure that rounding errors do not place us inside a block, because once inside a block, this algorithm no longer pushes the entity out of it
        // So lets collide with blocks a tiny bit earlier - i.e. by the amount of rounding error. In other words, lets push out the entity out of collision boxes once he gets within epsilon meters instead of 0 meters,
        // so that the position+motion addition at the end of the method never ends up being inside a block

        // A double value has ~15 digits. Our max map size of 64mil means we need 8 digits for the non-fractional part, leaving us with 7 digits for the fraction - so the rounding error is on the 8th digit
        // But for some reason we still clip through blocks if we use an epsilon that is less than 0.0001. Not sure why.
        double epsilon = 0.0001;
        double motEpsX = 0, motEpsY = 0, motEpsZ = 0;
        if (motionX > epsilon) motEpsX = epsilon;
        if (motionX < -epsilon) motEpsX = -epsilon;

        if (motionY > epsilon) motEpsY = epsilon;
        if (motionY < -epsilon) motEpsY = -epsilon;

        if (motionZ > epsilon) motEpsZ = epsilon;
        if (motionZ < -epsilon) motEpsZ = -epsilon;

        // We pretend we are by epsilon meters further and push the entity out of it
        // but at the end of the method we do not add this epsilon to the final position
        motionX += motEpsX;
        motionY += motEpsY;
        motionZ += motEpsZ;


        // Generate a cube that encompasses every block between the old and new position.
        // This could also just take the new position and old position without using motion.
        GenerateCollisionBoxList(instance, worldAccessor.BlockAccessor, motionX, motionY, motionZ, stepHeight, yExtra, entityPos.Dimension);

        bool collided = false;

        int collisionBoxListCount = instance.CollisionBoxList.Count;
        Cuboidd[] CollisionBoxListCuboids = instance.CollisionBoxList.cuboids;   // Local reference for efficiency

        double preCollisionMotionY = motionY;
        collBlockPos.SetDimension(entityPos.Dimension);
        // ---------- Y COLLISION. Call events and set collided vertically.
        for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
        {
            if (i >= collisionBoxListCount) break;
            motionY = CollisionBoxListCuboids[i].pushOutY(entityBox, motionY, ref pushDirection);
            if (pushDirection == EnumPushDirection.None) continue;

            collided = true;

            collBlockPos.Set(instance.CollisionBoxList.positions[i]);
            instance.CollisionBoxList.blocks[i].OnEntityCollide(
                worldAccessor,
                entity,
                collBlockPos,
                pushDirection == EnumPushDirection.Negative ? BlockFacing.UP : BlockFacing.DOWN,
                instance.tmpPosDelta.Set(motionX, motionY, motionZ),
                !entity.CollidedVertically
            );
        }
        entityBox.Translate(0, motionY, 0);

        entity.CollidedVertically = collided;
        if (collided && Math.Abs(motionY - preCollisionMotionY) > epsilon) motionY += motEpsY;   // Add back the epsilon, because it has gone as a result of the pushOutY call

        // Check if horizontal collision is possible.
        bool horizontallyBlocked = false;
        entityBox.Translate(motionX, 0, motionZ);
        foreach (var cuboid in instance.CollisionBoxList)
        {
            if (cuboid.Intersects(entityBox))
            {
                horizontallyBlocked = true;
                break;
            }
        }
        entityBox.Translate(-motionX, 0, -motionZ);  // cheaper than creating a new Cuboidd

        // No collisions for the entity found when testing horizontally, so skip this.
        // This allows entities to move around corners without falling down on a certain axis.
        collided = false;
        if (horizontallyBlocked)
        {
            // X - Collision (Horizontal)
            for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
            {
                if (i >= collisionBoxListCount) break;
                motionX = CollisionBoxListCuboids[i].pushOutX(entityBox, motionX, ref pushDirection);
                if (pushDirection == EnumPushDirection.None) continue;

                collided = true;

                collBlockPos.Set(instance.CollisionBoxList.positions[i]);
                instance.CollisionBoxList.blocks[i].OnEntityCollide(
                    worldAccessor,
                    entity,
                    collBlockPos,
                    pushDirection == EnumPushDirection.Negative ? BlockFacing.EAST : BlockFacing.WEST,
                    instance.tmpPosDelta.Set(motionX, motionY, motionZ),
                    !entity.CollidedHorizontally
                );
            }
            entityBox.Translate(motionX, 0, 0);

            // Z - Collision (Horizontal)

            for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
            {
                if (i >= collisionBoxListCount) break;
                motionZ = CollisionBoxListCuboids[i].pushOutZ(entityBox, motionZ, ref pushDirection);
                if (pushDirection == EnumPushDirection.None) continue;

                collided = true;

                collBlockPos.Set(instance.CollisionBoxList.positions[i]);
                instance.CollisionBoxList.blocks[i].OnEntityCollide(
                    worldAccessor,
                    entity,
                    collBlockPos,
                    pushDirection == EnumPushDirection.Negative ? BlockFacing.SOUTH : BlockFacing.NORTH,
                    instance.tmpPosDelta.Set(motionX, motionY, motionZ),
                    !entity.CollidedHorizontally
                );
            }
        }

        entity.CollidedHorizontally = collided;

        // fix for player on ladder clipping into block above issue  (caused by the .CollisionBox not always having height precisely 1.85)
        if (motionY > 0 && entity.CollidedVertically)
        {
            motionY -= entity.LadderFixDelta;
        }

        motionX -= motEpsX;
        motionY -= motEpsY;
        motionZ -= motEpsZ;

        newPosition.Set(pos.X + motionX, pos.Y + motionY, pos.Z + motionZ);
    }
    
    private static void GenerateCollisionBoxList(CollisionTester instance, IBlockAccessor blockAccessor, double motionX, double motionY, double motionZ, float stepHeight, float yExtra, int dimension)
    {
        double minx = double.MaxValue, miny = double.MaxValue, minz = double.MaxValue;
        double maxx = double.MinValue, maxy = double.MinValue, maxz = double.MinValue;
        
        for (int i = 0; i < count; i++)
        {
            var ebox = entityBox[i];
            minx = Math.Min(minx, ebox.X1);
            miny = Math.Min(miny, ebox.Y1);
            minz = Math.Min(minz, ebox.Z1);

            maxx = Math.Max(maxx, ebox.X2);
            maxy = Math.Max(maxy, ebox.Y2);
            maxz = Math.Max(maxz, ebox.Z2);
        }

        BlockPos minPos = new BlockPos();
        BlockPos maxPos = new BlockPos();
        BlockPos tmpPos = new BlockPos();
        // Check if the min and max positions of the collision test are unchanged and use the old list if they are.
        minPos.Set(
            (int)(minx + Math.Min(0, motionX)),
            (int)(miny + Math.Min(0, motionY) - yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences.
            (int)(minz + Math.Min(0, motionZ))
        );

        double y2 = Math.Max(miny + stepHeight, maxy);

        maxPos.Set(
            (int)(maxx + Math.Max(0, motionX)),
            (int)(y2 + Math.Max(0, motionY)),
            (int)(maxz + Math.Max(0, motionZ))
        );

        minPos.SetDimension(dimension);
        tmpPos.SetDimension(dimension);

        // Clear the list and add every cuboid the block has to it.
        instance.CollisionBoxList.Clear();
        blockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) => {
            Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, tmpPos.Set(x, y, z));
            if (collisionBoxes != null)
            {
                instance.CollisionBoxList.Add(collisionBoxes, x, y, z, block);
            }
        }, true);
    }
    
    private static Cuboidd SweptQuery(Cuboidd box, double mx, double my, double mz, double step, double yExtra)
    {
        Cuboidd q = box.Clone();
        q.X1 += Math.Min(0.0, mx); q.X2 += Math.Max(0.0, mx);
        q.Z1 += Math.Min(0.0, mz); q.Z2 += Math.Max(0.0, mz);
        q.Y2 = Math.Max(box.Y2 + Math.Max(0.0, my), box.Y1 + step);
        q.Y1 += Math.Min(0.0, my) - yExtra;
        return q;
    }
}*/