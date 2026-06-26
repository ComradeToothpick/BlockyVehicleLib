using System;
using BlockyVehicleLib.Entities;
using BlockyVehicleLib.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;


namespace BlockyVehicleLib.Entities

#nullable disable

{
    public enum EnumIntersect
    {
        NoIntersect,
        IntersectX,
        IntersectY,
        IntersectZ,
        Stuck
    }

    public class VehicleCollisionTester : CollisionTester
    {
        //Completed (?) collision detection for entities (Needs testing)
        //TODO: Implement Collision detection for vehicles
        //public CachedCuboidListFaster CollisionBoxList = new();
        public CachedPsuedoCuboidListFaster CollisionBoxList2 = new();

        //public Cuboidd entityBox = new();
        public PsuedoCuboidd sudoBox = new PsuedoCuboidd();
        // Use class level fields to reduce garbage collection
        //public BlockPos tmpPos = new(Dimensions.WillSetLater);
        //public Vec3d tmpPosDelta = new();

        //public BlockPos minPos = new(Dimensions.WillSetLater);
        //public BlockPos maxPos = new(Dimensions.WillSetLater);

        //public Vec3d pos = new();
        public Vec3d cPos = new();
        
        readonly Cuboidd tmpBox = new();
        readonly BlockPos blockPos = new(Dimensions.WillSetLater);
        readonly Vec3d blockPosVec = new();
        readonly BlockPos collBlockPos = new(Dimensions.WillSetLater);
        private ICoreAPI api;

        /// <summary>
        /// Takes the entity positiona and motion and adds them, respecting any colliding blocks. The resulting new position is put into outposition
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="entityPos"></param>
        /// <param name="dtFactor"></param>
        /// <param name="newPosition"></param>
        /// <param name="stepHeight"></param>
        /// <param name="yExtra">Default 1 for the extra high collision boxes of fences</param>
        public void ApplyTerrainCollision(Entity entity, EntityPos entityPos,
            float dtFactor, ref Vec3d newPosition, float stepHeight = 1, float yExtra = 1)
        {
            if (this.api == null)
            {
                this.api = entity.Api;
            }
            minPos.SetDimension(entityPos.Dimension);

            var worldAccessor = entity.World;
            Vec3d pos = this.pos; // Local copy for efficiency
            Cuboidd entityBox = this.entityBox; // Local copy for efficiency
            PsuedoCuboidd sudoBox = this.sudoBox;
            
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
            GenerateCollisionBoxList(worldAccessor.BlockAccessor, motionX, motionY, motionZ, stepHeight, yExtra,
                entityPos.Dimension);

            bool collided = false;

            int collisionBoxListCount = CollisionBoxList.Count;
            Cuboidd[] CollisionBoxListCuboids = CollisionBoxList.cuboids; // Local reference for efficiency
            
            double preCollisionMotionY = motionY;
            collBlockPos.SetDimension(entityPos.Dimension);
            // ---------- Y COLLISION. Call events and set collided vertically.
            for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
            {
                if (i >= collisionBoxListCount) break;
                motionY = CollisionBoxListCuboids[i].pushOutY(entityBox, motionY, ref pushDirection);
                if (pushDirection == EnumPushDirection.None) continue;

                collided = true;

                collBlockPos.Set(CollisionBoxList.positions[i]);
                CollisionBoxList.blocks[i].OnEntityCollide(
                    worldAccessor,
                    entity,
                    collBlockPos,
                    pushDirection == EnumPushDirection.Negative ? BlockFacing.UP : BlockFacing.DOWN,
                    tmpPosDelta.Set(motionX, motionY, motionZ),
                    !entity.CollidedVertically
                );
            }

            entityBox.Translate(0, motionY, 0);

            entity.CollidedVertically = collided;
            if (collided && Math.Abs(motionY - preCollisionMotionY) > epsilon)
                motionY += motEpsY; // Add back the epsilon, because it has gone as a result of the pushOutY call

            // Check if horizontal collision is possible.
            bool horizontallyBlocked = false;
            entityBox.Translate(motionX, 0, motionZ);
            foreach (var cuboid in CollisionBoxList)
            {
                if (cuboid.Intersects(entityBox))
                {
                    horizontallyBlocked = true;
                    break;
                }
            }

            entityBox.Translate(-motionX, 0, -motionZ); // cheaper than creating a new Cuboidd

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

                    collBlockPos.Set(CollisionBoxList.positions[i]);
                    CollisionBoxList.blocks[i].OnEntityCollide(
                        worldAccessor,
                        entity,
                        collBlockPos,
                        pushDirection == EnumPushDirection.Negative ? BlockFacing.EAST : BlockFacing.WEST,
                        tmpPosDelta.Set(motionX, motionY, motionZ),
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

                    collBlockPos.Set(CollisionBoxList.positions[i]);
                    CollisionBoxList.blocks[i].OnEntityCollide(
                        worldAccessor,
                        entity,
                        collBlockPos,
                        pushDirection == EnumPushDirection.Negative ? BlockFacing.SOUTH : BlockFacing.NORTH,
                        tmpPosDelta.Set(motionX, motionY, motionZ),
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

        /// <summary>
        /// Takes the entity position and motion and adds them, respecting any colliding blocks. The resulting new position is put into outposition
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="entityPos"></param>
        /// <param name="entityVehiclePosList"></param>
        /// <param name="dtFactor"></param>
        /// <param name="newPosition"></param>
        /// <param name="subDimensionId"></param>
        /// <param name="stepHeight"></param>
        /// <param name="yExtra">Default 1 for the extra high collision boxes of fences</param>
        public void ApplyTerrainCollision(
            Entity entity,
            EntityPos entityPos,
            EntityPos[] entityVehiclePosList,//added
            float dtFactor,
            ref Vec3d newPosition,
            int[] subDimensionId,//added
            float stepHeight = 1f,
            float yExtra = 1f)
        {
            if (this.api == null)
            {
                this.api = entity.Api;
            }
            EntityPos convPos;
            this.minPos.SetDimension(entityPos.Dimension);
            var worldAccessor = entity.World;
            Vec3d pos = this.pos;
            Vec3d cPos = this.cPos;
            Cuboidd entityBox = this.entityBox;
            
            pos.X = entityPos.X;
            pos.Y = entityPos.Y;
            pos.Z = entityPos.Z;

            Vec3d finalMotion = entityPos.Motion * dtFactor;
            
            EnumPushDirection pushDirection = EnumPushDirection.None;
            
            entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);
            this.GenerateCollisionBoxList(worldAccessor.BlockAccessor, finalMotion.X, finalMotion.Y, finalMotion.Z, stepHeight, yExtra,
                entityPos.Dimension, entityPos, subDimensionId, entityVehiclePosList);
            bool collided = false;
            int collisionBoxListCount = CollisionBoxList.Count;
            Cuboidd[] CollisionBoxListCuboids = CollisionBoxList.cuboids; // Local reference for efficiency
            // ---------- Y COLLISION. Call events and set collided vertically.
            for (int j = 0; j < entityVehiclePosList.Length; j ++) 
            {
                double motionX = finalMotion.X;
                double motionY = finalMotion.Y;
                double motionZ = finalMotion.Z;
                double epsilon = 0.0001;
                double motEpsX = 0, motEpsY = 0, motEpsZ = 0;
            
                // We need to make sure that rounding errors do not place us inside a block, because once inside a block, this algorithm no longer pushes the entity out of it
                // So lets collide with blocks a tiny bit earlier - i.e. by the amount of rounding error. In other words, lets push out the entity out of collision boxes once he gets within epsilon meters instead of 0 meters,
                // so that the position+motion addition at the end of the method never ends up being inside a block

                // A double value has ~15 digits. Our max map size of 64mil means we need 8 digits for the non-fractional part, leaving us with 7 digits for the fraction - so the rounding error is on the 8th digit
                // But for some reason we still clip through blocks if we use an epsilon that is less than 0.0001. Not sure why.
            

                // We pretend we are by epsilon meters further and push the entity out of it
                // but at the end of the method we do not add this epsilon to the final position
                
                double preCollisionMotionY = motionY;
                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)//I suspect this could get quite laggy with more than a few vehicles nearby, perhaps some matrix maths could help here down the line?
                {
                    //CollisionBoxListCuboids.Append<Cuboidd>(CollisionBoxList2.cuboids);
                    collBlockPos.SetDimension(entityPos.Dimension);
                    if (collisionBoxListCount == 0) break;
                    EntityPos vehiclePos = entityVehiclePosList[j];
                    Vec3d relVel = FindRelativeVelocity(entityPos, vehiclePos);
                    EntityPos relPos = FindRelativeEntityPosition(entity.Pos, vehiclePos);
                
                    relPos.Motion.X = relVel.X * dtFactor;
                    relPos.Motion.Y = relVel.Y * dtFactor;
                    relPos.Motion.Z = relVel.Z * dtFactor;
                
                    motEpsX = 0;
                    motEpsY = 0;
                    motEpsZ = 0;
                
                    if (relVel.X > epsilon) motEpsX = epsilon;
                    if (relVel.X < -epsilon) motEpsX = -epsilon;
                
                    if (relVel.Y > epsilon) motEpsY = epsilon;
                    if (relVel.Y < -epsilon) motEpsY = -epsilon;
                
                    if (relVel.Z > epsilon) motEpsZ = epsilon;
                    if (relVel.Z < -epsilon) motEpsZ = -epsilon;
                
                    relVel.X += motEpsX;
                    relVel.Y += motEpsY;
                    relVel.Z += motEpsZ;
                
                    Quaterniond temp = new Quaterniond(); //Why is this not static? It should be static!
                    PsuedoCuboidd tempBox = this.sudoBox; //Local reference for efficiency

                    tempBox.rotation = temp.Conjugate(new double[4],
                        PsuedoCuboidd.ConvertEulerAngles(vehiclePos.Pitch, vehiclePos.Yaw, vehiclePos.Roll));
                    tempBox.pos.X = relPos.X +
                                    (int)(subDimensionId[j] % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
                    tempBox.pos.Y = relPos.Y + 8192;
                    tempBox.pos.Z = relPos.Z +
                                    (int)(subDimensionId[j] / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);

                    tempBox.size.X = entity.CollisionBox.Width;
                    tempBox.size.Y = entity.CollisionBox.Height;
                    tempBox.size.Z = entity.CollisionBox.Length;
                
                    tempBox.SetInternalCorners();
                    tempBox.GetExternalCorners();
                
                    motionY = tempBox.pushOutY(entityBox, motionY, ref pushDirection);
                    if (pushDirection == EnumPushDirection.None) continue;
                
                    collided = true;
                
                    collBlockPos.Set(CollisionBoxList.positions[i]);
                    CollisionBoxList.blocks[i].OnEntityCollide(
                        worldAccessor,
                        entity,
                        collBlockPos,
                        pushDirection == EnumPushDirection.Negative ? BlockFacing.UP : BlockFacing.DOWN,
                        tmpPosDelta.Set(motionX, motionY, motionZ),
                        !entity.CollidedVertically
                    );
                    entityBox.Translate(0, motionY, 0);

                    entity.CollidedVertically = collided;
                    if (collided && Math.Abs(motionY - preCollisionMotionY) > epsilon)
                        motionY += motEpsY; // Add back the epsilon, because it has gone as a result of the pushOutY call

                    // Check if horizontal collision is possible.
                    bool horizontallyBlocked = false;
                    entityBox.Translate(motionX, 0, motionZ);
                    foreach (var cuboid in CollisionBoxList)
                    {
                        if (cuboid.Intersects(entityBox))
                        {
                            horizontallyBlocked = true;
                            break;
                        }
                    }

                    entityBox.Translate(-motionX, 0, -motionZ); // cheaper than creating a new Cuboidd

                    // No collisions for the entity found when testing horizontally, so skip this.
                    // This allows entities to move around corners without falling down on a certain axis.
                    collided = false;
                    if (horizontallyBlocked)
                    {
                        // X - Collision (Horizontal)
                        for (int k = 0; k < CollisionBoxListCuboids.Length; k++)
                        {
                            if (k >= collisionBoxListCount) break;
                            motionX = CollisionBoxListCuboids[k].pushOutX(entityBox, motionX, ref pushDirection);
                            if (pushDirection == EnumPushDirection.None) continue;

                            collided = true;

                            collBlockPos.Set(CollisionBoxList.positions[k]);
                            CollisionBoxList.blocks[k].OnEntityCollide(
                                worldAccessor,
                                entity,
                                collBlockPos,
                                pushDirection == EnumPushDirection.Negative ? BlockFacing.EAST : BlockFacing.WEST,
                                tmpPosDelta.Set(motionX, motionY, motionZ),
                                !entity.CollidedHorizontally
                            );
                        }

                        entityBox.Translate(motionX, 0, 0);

                        // Z - Collision (Horizontal)

                        for (int k = 0; k < CollisionBoxListCuboids.Length; k++)
                        {
                            if (k >= collisionBoxListCount) break;
                            motionZ = CollisionBoxListCuboids[k].pushOutZ(entityBox, motionZ, ref pushDirection);
                            if (pushDirection == EnumPushDirection.None) continue;

                            collided = true;

                            collBlockPos.Set(CollisionBoxList.positions[k]);
                            CollisionBoxList.blocks[k].OnEntityCollide(
                                worldAccessor,
                                entity,
                                collBlockPos,
                                pushDirection == EnumPushDirection.Negative ? BlockFacing.SOUTH : BlockFacing.NORTH,
                                tmpPosDelta.Set(motionX, motionY, motionZ),
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

                    if (motionX < 0)
                    {
                        finalMotion.X = Math.Max(motionX, finalMotion.X);
                    }
                    if (motionX >= 0)
                    {
                        finalMotion.X = Math.Min(motionX, finalMotion.X);
                    }
                    if (motionY < 0)
                    {
                        finalMotion.Y = Math.Max(motionY, finalMotion.Y);
                    }
                    if (motionY >= 0)
                    {
                        finalMotion.Y = Math.Min(motionY, finalMotion.Y);
                    }
                    if (motionZ < 0)
                    {
                        finalMotion.Z = Math.Max(motionZ, finalMotion.Z);
                    }
                    if (motionZ >= 0)
                    {
                        finalMotion.Z = Math.Min(motionZ, finalMotion.Z);
                    }
                }
            }
            newPosition.Set(pos.X + finalMotion.X, pos.Y + finalMotion.Y, pos.Z + finalMotion.Z);
        }

        /*protected virtual void GenerateCollisionBoxList(
            IBlockAccessor blockAccessor,
            double motionX,
            double motionY,
            double motionZ,
            float stepHeight,
            float yExtra,
            int dimension)
        {
            int num1 = this.minPos.SetAndEquals((int)(this.entityBox.X1 + Math.Min(0.0, motionX)),
                (int)(this.entityBox.Y1 + Math.Min(0.0, motionY) - (double)yExtra),
                (int)(this.entityBox.Z1 + Math.Min(0.0, motionZ)))
                ? 1
                : 0;
            double num2 = Math.Max(this.entityBox.Y1 + (double)stepHeight, this.entityBox.Y2);
            int num3 = this.maxPos.SetAndEquals((int)(this.entityBox.X2 + Math.Max(0.0, motionX)),
                (int)(num2 + Math.Max(0.0, motionY)), (int)(this.entityBox.Z2 + Math.Max(0.0, motionZ)))
                ? 1
                : 0;
            if ((num1 & num3) != 0)
                return;
            this.CollisionBoxList.Clear();
            blockAccessor.WalkBlocks(this.minPos, this.maxPos, (Action<Block, int, int, int>)((block, x, y, z) =>
            {
                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, this.tmpPos.Set(x, y, z));
                if (collisionBoxes == null)
                    return;
                this.CollisionBoxList.Add(collisionBoxes, x, y, z, block);
            }), true);
        }*/

        

        protected override void GenerateCollisionBoxList(IBlockAccessor blockAccessor, double motionX, double motionY,
            double motionZ, float stepHeight, float yExtra, int dimension)
        {
            // NEVER CALLED IN 1.20 as all invocations are in CachingCollisionTester.  But we retain this in case a mod calls it.

            // Check if the min and max positions of the collision test are unchanged and use the old list if they are.
            bool minPosIsUnchanged = minPos.SetAndEquals(
                (int)(entityBox.X1 + Math.Min(0, motionX)),
                (int)(entityBox.Y1 + Math.Min(0, motionY) -
                      yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences.
                (int)(entityBox.Z1 + Math.Min(0, motionZ))
            );

            double y2 = Math.Max(entityBox.Y1 + stepHeight, entityBox.Y2);

            bool maxPosIsUnchanged = maxPos.SetAndEquals(
                (int)(entityBox.X2 + Math.Max(0, motionX)),
                (int)(y2 + Math.Max(0, motionY)),
                (int)(entityBox.Z2 + Math.Max(0, motionZ))
            );

            if (minPosIsUnchanged && maxPosIsUnchanged) return;

            // Clear the list and add every cuboid the block has to it.
            CollisionBoxList.Clear();
            blockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) =>
            {
                Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, tmpPos.Set(x, y, z));
                if (collisionBoxes != null)
                {
                    CollisionBoxList.Add(collisionBoxes, x, y, z, block);
                }
            }, true);
        }

        //Need to completely redo this
        protected virtual void GenerateCollisionBoxList(
            IBlockAccessor blockAccessor,
            double motionX,
            double motionY,
            double motionZ,
            float stepHeight,
            float yExtra,
            int dimension,
            EntityPos entityPos,
            int[] subDimensionId,
            EntityPos[] entityPosList
        )
        {
            EntityPos baseEntityPos = new EntityPos(0, 0, 0);
            baseEntityPos.Motion.X = motionX;
            baseEntityPos.Motion.Y = motionY;
            baseEntityPos.Motion.Z = motionZ;
            Vec3d[] entityVec1List = FindRelativePosition(entityPosList, new Vec3d(this.entityBox.X1, this.entityBox.Y1, this.entityBox.Z1));
            Vec3d[] entityVec2List = FindRelativePosition(entityPosList, new Vec3d(this.entityBox.X2, this.entityBox.Y2, this.entityBox.Z2));
            for (int i = 0; i < subDimensionId.Length; i++)
            {
                Vec3d relativeMotion = FindRelativeVelocity(entityPos, entityPosList[i]);
                int num1 = this.minPos.SetAndEquals((int)(entityVec1List[i].X + Math.Min(0.0, relativeMotion.X) + (int)(subDimensionId[i] % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/)),
                    (int)(entityVec1List[i].Y + Math.Min(0.0, relativeMotion.Y) - (double)yExtra + 8192 /*0x2000*/),
                    (int)(entityVec1List[i].Z + Math.Min(0.0, relativeMotion.Z) + (int)(subDimensionId[i] / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/)))
                    ? 1
                    : 0;
                double num2 = Math.Max(entityVec1List[i].Y + (double)stepHeight, entityVec2List[i].Y);
                int num3 = this.maxPos.SetAndEquals((int)(entityVec2List[i].X + Math.Max(0.0, relativeMotion.X) + (int)(subDimensionId[i] % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/)),
                    (int)(num2 + Math.Max(0.0, relativeMotion.Y) + 8192 /*0x2000*/), (int)(entityVec2List[i].Z + Math.Max(0.0, relativeMotion.Z) + (int)(subDimensionId[i] / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/)))
                    ? 1
                    : 0;
                if ((num1 & num3) != 0)
                    return;
                this.CollisionBoxList.Clear();
                blockAccessor.WalkBlocks(this.minPos, this.maxPos, (Action<Block, int, int, int>)((block, x, y, z) =>
                {
                    Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, this.tmpPos.Set(x, y, z));
                    if (collisionBoxes != null) this.CollisionBoxList.Add(collisionBoxes, x, y, z, block);
                    api.Logger.Event("Collision box list generated: " + CollisionBoxList.Count + " Collision boxes added.");
                    this.tmpPos.dimension = dimension;
                    
                    if (collisionBoxes == null) return;
                }), true);
            }
        }

        /// <summary>
        /// Tests given cuboidf collides with the terrain. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns></returns>
        public bool IsColliding(IBlockAccessor blockAccessor, PsuedoCuboidd entityBoxRel, Vec3d pos,
            bool alsoCheckTouch = true)
        {
            return GetCollidingBlock(blockAccessor, entityBoxRel, pos, alsoCheckTouch) != null;
        }
        
        
        new public Block GetCollidingBlock(IBlockAccessor blockAccessor, PsuedoCuboidd entityBoxRel, Vec3d pos,
            bool alsoCheckTouch = true)
        {
            PsuedoCuboidd entityBox = sudoBox.SetAndTranslate(entityBoxRel, pos);

            int minX = (int)entityBox.X1;
            int minY = (int)entityBox.Y1 - 1; // -1 for the extra high collision box of fences.
            int minZ = (int)entityBox.Z1;

            int maxX = (int)entityBox.X2;
            int maxY = (int)entityBox.Y2;
            int maxZ = (int)entityBox.Z2;

            entityBox.pos.Y =
                Math.Round(entityBox.pos.Y,
                    5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            BlockPos blockPos = this.blockPos; // Local reference for efficiency
            Vec3d blockPosVec = this.blockPosVec; // Local reference for efficiency
            for (int y = minY; y <= maxY; y++)
            {
                blockPos.SetAndCorrectDimension(minX, y, minZ);
                blockPosVec.Set(minX, y, minZ);
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.X = x;
                    blockPosVec.X = x;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
                        Block block = blockAccessor.GetBlock(blockPos, BlockLayersAccess.MostSolid);

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        
                        if (collisionBoxes == null || collisionBoxes.Length == 0) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            if (alsoCheckTouch
                                    ? entityBox.IntersectsOrTouches(collBox, blockPosVec)
                                    : entityBox.Intersects(collBox, blockPosVec)) return block;
                        }
                    }
                }
            }
            return null;
        }
        
        
        public bool IsCollidingVehicle(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, EntityVehicle[] nearbyVehicles,
            bool alsoCheckTouch = true)
        {
            return GetCollidingVehicleBlocks(blockAccessor, entityBoxRel, pos, nearbyVehicles, alsoCheckTouch) != null;
        }
        
        public Block GetCollidingVehicleBlocks(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos, 
            EntityVehicle[] nearbyVehicles, bool alsoCheckTouch = true)
        {
            //This I think is finished now?
            //Convert the real position of the vehicle to the corresponding blockpos of the minidimension
            
            PsuedoCuboidd entityBox = sudoBox.SetFromCuboidf(entityBoxRel, pos);
            //EntityChunky[] nearbyVehicles = PsuedoCuboidd.FindNearbyVehicles(pos, vehicleList);
            
            Vec3d[] relativePos = FindRelativePosition(nearbyVehicles, pos);
            
            double[,] tmpRotation = new double[nearbyVehicles.Length, 4];
            int subId;
            if (nearbyVehicles != null && nearbyVehicles.Length > 0)
            {
                Block[] vehicleBlocks = new Block[nearbyVehicles.Length];
                BlockPos vehicleBlockPos;
                for (int i = 0; i < nearbyVehicles.Length; i++)
                {
                    double[] rot = PsuedoCuboidd.ConvertEulerAngles(nearbyVehicles[i].Pos.Pitch,
                        nearbyVehicles[i].Pos.Yaw, nearbyVehicles[i].Pos.Roll);
                    double[] conjugate = Quaterniond.FromValues(-rot[0], -rot[1], -rot[2], rot[3]);
                    for (int j = 0; j < 4; j++)
                        tmpRotation[i, j] = conjugate[j];
                    subId = (nearbyVehicles[i].WatchedAttributes.GetAttribute("dim") as IntAttribute).value;
                    relativePos[i].X +=
                        (int)(subId % 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
                    
                    relativePos[i].Y += (int)(8192) /*0x2000*/;
                    
                    relativePos[i].Z +=
                        (int)(subId / 4096 /*0x1000*/ * 16384 /*0x4000*/ + 8192 /*0x2000*/);
                    vehicleBlockPos = new BlockPos((int)relativePos[i].X, (int)relativePos[i].Y, (int)relativePos[i].Z);
                    
                    vehicleBlocks[i] = blockAccessor.GetBlock(vehicleBlockPos, BlockLayersAccess.MostSolid);
                }
            }
            else
            {
                return null;
            }
            
            entityBox.pos.Y =
                Math.Round(entityBox.pos.Y,
                    5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            BlockPos blockPos = this.blockPos; // Local reference for efficiency
            Vec3d blockPosVec = this.blockPosVec; // Local reference for efficiency
            
            //The entityBox needs to be rotated and operate in the minidimension to match the rotation of the vehicle.
            //The rotation of the entityBox should be the conjugate of the vehicle's rotation.
            for (int i = 0; i < nearbyVehicles.Length; i++)
            {
                double[] rot = { tmpRotation[i, 0], tmpRotation[i, 1], tmpRotation[i, 2], tmpRotation[i, 3] };
                entityBox.SetRotation(rot);
                entityBox.GetExternalCorners();
                //These need to be calculated for each nearbyVehicle after the vehicle rotations
                //have been collected and the conjugate applied to the entityBox
                int minX = (int)entityBox.X1;
                int minY = (int)entityBox.Y1 - 1; // -1 for the extra high collision box of fences.
                int minZ = (int)entityBox.Z1;
            
                int maxX = (int)entityBox.X2;
                int maxY = (int)entityBox.Y2;
                int maxZ = (int)entityBox.Z2;
                for (int y = (int)minY; y <= (int)maxY; y++)
                {
                    blockPos.SetAndCorrectDimension(minX, minY, minZ);
                    blockPosVec.Set(minX, y, minZ);
                    for (int x = minX; x <= maxX; x++)
                    {
                        blockPos.X = x;
                        blockPosVec.X = x;
                        for (int z = minZ; z <= maxZ; z++)
                        {
                            blockPos.Z = z;
                            Block block = blockAccessor.GetBlock(blockPos, BlockLayersAccess.MostSolid);

                            PsuedoCuboidd[] collisionBoxes = PsuedoCuboidd.CollectCuboidf(block.GetCollisionBoxes(blockAccessor, blockPos), entityBox.rotation);
                        
                            if (collisionBoxes == null || collisionBoxes.Length == 0) continue;

                            blockPosVec.Z = z;
                            for (int j = 0; j < collisionBoxes.Length; j++)
                            {
                                PsuedoCuboidd collBox = collisionBoxes[j];
                                if (collBox == null) continue;

                                if (alsoCheckTouch
                                        ? entityBox.IntersectsOrTouches(collBox, blockPosVec)
                                        : entityBox.Intersects(collBox, blockPosVec)) return block;
                            }
                        }
                    }
                }
            }
            return null;
        }

        public Cuboidd GetCollidingCollisionBox(IBlockAccessor blockAccessor, PsuedoCuboidd entityBoxRel, Vec3d pos,
            bool alsoCheckTouch = true)
        {
            BlockPos blockPos = new(Dimensions.NormalWorld); // The dimension is included in the Vec3d pos.Y field
            Vec3d blockPosVec = new();
            PsuedoCuboidd entityBox = entityBoxRel.Translate(pos);

            entityBox.pos.Y =
                Math.Round(entityBox.pos.Y,
                    5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            int minX = (int)(entityBoxRel.X1 + pos.X);
            int minY = (int)(entityBoxRel.Y1 + pos.Y - 1); // -1 for the extra high collision box of fences
            int minZ = (int)(entityBoxRel.Z1 + pos.Z);

            int maxX = (int)Math.Ceiling(entityBoxRel.X2 + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.Y2 + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.Z2 + pos.Z);

            for (int y = minY; y <= maxY; y++)
            {
                blockPos.Set(minX, y, minZ);
                blockPosVec.Set(minX, y, minZ);
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.X = x;
                    blockPosVec.X = x;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
#pragma warning disable CS0618 // Type or member is obsolete - but it's correct here as the dimension is included in the y field
                        Block block = blockAccessor.GetMostSolidBlock(x, y, z);
#pragma warning restore CS0618

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch
                                ? entityBox.IntersectsOrTouches(collBox, blockPosVec)
                                : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                return collBox.ToDouble().Translate(blockPos);
                            }
                        }
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// If given cuboidf collides with the terrain, returns the collision box it collides with. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="pos"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <returns></returns>
        public Cuboidd GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, Vec3d pos,
            bool alsoCheckTouch = true)
        {
            BlockPos blockPos = new(Dimensions.NormalWorld); // The dimension is included in the Vec3d pos.Y field
            Vec3d blockPosVec = new();
            Cuboidd entityBox = entityBoxRel.ToDouble().Translate(pos);

            entityBox.Y1 =
                Math.Round(entityBox.Y1,
                    5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.

            int minX = (int)(entityBoxRel.X1 + pos.X);
            int minY = (int)(entityBoxRel.Y1 + pos.Y - 1); // -1 for the extra high collision box of fences
            int minZ = (int)(entityBoxRel.Z1 + pos.Z);

            int maxX = (int)Math.Ceiling(entityBoxRel.X2 + pos.X);
            int maxY = (int)Math.Ceiling(entityBoxRel.Y2 + pos.Y);
            int maxZ = (int)Math.Ceiling(entityBoxRel.Z2 + pos.Z);

            
            
            for (int y = minY; y <= maxY; y++)
            {
                blockPos.Set(minX, y, minZ);
                blockPosVec.Set(minX, y, minZ);
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.X = x;
                    blockPosVec.X = x;
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
#pragma warning disable CS0618 // Type or member is obsolete - but it's correct here as the dimension is included in the y field
                        Block block = blockAccessor.GetMostSolidBlock(x, y, z);
#pragma warning restore CS0618

                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch
                                ? entityBox.IntersectsOrTouches(collBox, blockPosVec)
                                : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                return collBox.ToDouble().Translate(blockPos);
                            }
                        }
                    }
                }
            }

            return null;
        }
        //Should update this to use a PsuedoCuboidd for the entity collision box so it can work for vehicles
        //Should also update this to account for angular velocity of the vehicle
        /// <summary>
        /// Tests given cuboidf collides with the terrain. By default also checks if the cuboid is merely touching the terrain, set alsoCheckTouch to disable that.
        /// <br/>NOTE: currently not dimension-aware unless the supplied Vec3d pos is dimension-aware
        /// </summary>
        /// <param name="blockAccessor"></param>
        /// <param name="entityBoxRel"></param>
        /// <param name="convPos"></param>
        /// <param name="intoCuboid"></param>
        /// <param name="subDimensionId"></param>
        /// <param name="vehicleRotation"></param>
        /// <param name="alsoCheckTouch"></param>
        /// <param name="dimension"></param>
        /// <returns></returns>
        public bool GetCollidingCollisionBox(IBlockAccessor blockAccessor, Cuboidf entityBoxRel, EntityPos convPos,
            ref Cuboidd intoCuboid, int subDimensionId, double[] vehicleRotation, bool alsoCheckTouch = true, int dimension = 1)
        {
            BlockPos blockPos = new(dimension);
            Vec3d blockPosVec = new();
            PsuedoCuboidd entityBox = new PsuedoCuboidd();
            entityBox.SetFromCuboidf(entityBoxRel, convPos);
            
            entityBox.pos.Y =
                Math.Round(entityBox.pos.Y,
                    5); // Fix float/double rounding errors. Only need to fix the vertical because gravity.
            
            int minX = (int)(entityBox.X1);
            int minY = (int)(entityBox.Y1 - 1); // -1 for the extra high collision box of fences.
            int minZ = (int)(entityBox.Z1);

            int maxX = (int)Math.Ceiling(entityBox.X2);
            int maxY = (int)Math.Ceiling(entityBox.Y2);
            int maxZ = (int)Math.Ceiling(entityBox.Z2);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    blockPos.Set(x, y, minZ);
                    blockPosVec.Set(x, y, minZ);
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        blockPos.Z = z;
                        Block block = blockAccessor.GetBlock(blockPos, BlockLayersAccess.MostSolid);
                        
                        Cuboidf[] collisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);
                        if (collisionBoxes == null) continue;

                        blockPosVec.Z = z;
                        for (int i = 0; i < collisionBoxes.Length; i++)
                        {
                            Cuboidf collBox = collisionBoxes[i];
                            if (collBox == null) continue;

                            bool colliding = alsoCheckTouch
                                ? entityBox.IntersectsOrTouches(collBox, blockPosVec)
                                : entityBox.Intersects(collBox, blockPosVec);
                            if (colliding)
                            {
                                intoCuboid.Set(collBox).Translate(blockPos);
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
        
        public Vec3d[] FindRelativePosition(EntityPos[] VehiclePosList, Vec3d pos)
        {
            //This needs to account for the rotation of the entity to find the correct local position relative to each Vehicle
            
            Vec3d[] relativePos = new Vec3d[VehiclePosList.Length];
            for (int i = 0; i < VehiclePosList.Length; i++)
            {
                relativePos[i] = new Vec3d();
                double[] rotation = PsuedoCuboidd.ConvertEulerAngles(VehiclePosList[i].Pitch, VehiclePosList[i].Yaw, VehiclePosList[i].Roll);
                Quaterniond.Normalize(rotation, rotation);
                double[] rotStar = [-rotation[0], -rotation[1], -rotation[2], rotation[3]];
                double[] qPos = [pos.X, pos.Y, pos.Z, 0];
                Quaterniond.Multiply(rotation, rotation, qPos);
                Quaterniond.Multiply(rotation, rotation, rotStar);
                relativePos[i].X = VehiclePosList[i].X - rotation[0];
                relativePos[i].Y = VehiclePosList[i].Y - rotation[1];
                relativePos[i].Z = VehiclePosList[i].Z - rotation[2];
            }
            return relativePos;
        }
  
        public static Vec3d FindRelativePosition(EntityPos VehiclePos, EntityPos entityPos)
        {
            Vec3d relativePos = new Vec3d();
            
            double[] rotation = PsuedoCuboidd.ConvertEulerAngles(VehiclePos.Pitch, VehiclePos.Yaw, VehiclePos.Roll);
            Quaterniond.Normalize(rotation, rotation);
            double[] rotStar = [-rotation[0], -rotation[1], -rotation[2], rotation[3]];
            double[] qPos = [entityPos.X, entityPos.Y, entityPos.Z, 0];
            Quaterniond.Multiply(rotation, rotation, qPos);
            Quaterniond.Multiply(rotation, rotation, rotStar);
    
            relativePos.X = VehiclePos.X - rotation[0];
            relativePos.Y = VehiclePos.Y - rotation[1];
            relativePos.Z = VehiclePos.Z - rotation[2];
    
            return relativePos;
        }
        
        public static EntityPos FindRelativeEntityPosition(EntityPos VehiclePos, EntityPos entityPos)
        {
            //entityPos is assumed to have Identity rotation
            EntityPos relativePos = new EntityPos();
            
            double[] rotation = PsuedoCuboidd.ConvertEulerAngles(VehiclePos.Pitch, VehiclePos.Yaw, VehiclePos.Roll);
            Quaterniond.Normalize(rotation, rotation);
            double[] rotStar = [-rotation[0], -rotation[1], -rotation[2], rotation[3]];
            double[] qPos = [entityPos.X, entityPos.Y, entityPos.Z, 0];
            Quaterniond.Multiply(rotation, rotation, qPos);
            Quaterniond.Multiply(rotation, rotation, rotStar);
    
            relativePos.X = VehiclePos.X - rotation[0];
            relativePos.Y = VehiclePos.Y - rotation[1];
            relativePos.Z = VehiclePos.Z - rotation[2];
            float[] eulerAngles = Quaterniond.ToEulerAngles(rotStar);
            relativePos.Pitch = eulerAngles[0];
            relativePos.Yaw = eulerAngles[1]; 
            relativePos.Roll = eulerAngles[2];
            relativePos.Motion = FindRelativeVelocity(entityPos, VehiclePos);
            return relativePos;
        }
        
        public Vec3d[] FindRelativePosition(EntityVehicle[] VehicleList, Vec3d pos)
        {
            //This needs to account for the rotation of the entity to find the correct local position relative to each Vehicle
            
            Vec3d[] relativePos = new Vec3d[VehicleList.Length];
            for (int i = 0; i < VehicleList.Length; i++)
            {
                double[] rotation = PsuedoCuboidd.ConvertEulerAngles(VehicleList[i].Pos.Pitch, VehicleList[i].Pos.Yaw, VehicleList[i].Pos.Roll);
                Quaterniond.Normalize(rotation, rotation);
                double[] rotStar = [-rotation[0], -rotation[1], -rotation[2], rotation[3]];
                double[] qPos = [pos.X, pos.Y, pos.Z, 0];
                Quaterniond.Multiply(rotation, rotation, qPos);
                Quaterniond.Multiply(rotation, rotation, rotStar);
                relativePos[i].X = VehicleList[i].Pos.X - rotation[0];
                relativePos[i].Y = VehicleList[i].Pos.Y - rotation[1];
                relativePos[i].Z = VehicleList[i].Pos.Z - rotation[2];
            }
            return relativePos;
        }

        public static Vec3d FindRelativeVelocity(EntityPos entityPosA, EntityPos entityPosB)
        {
            EntityPos tempA = entityPosA.Copy();
            EntityPos tempB = entityPosB.Copy();
            //double[] rotA = PsuedoCuboidd.ConvertEulerAngles(tempA.Pitch, tempA.Yaw, tempA.Roll);
            double[] rot = PsuedoCuboidd.ConvertEulerAngles(tempB.Pitch, tempB.Yaw, tempB.Roll);
            Quaterniond.Normalize(rot, rot);
            double[] rotStar =  [-rot[0], -rot[1], -rot[2], rot[3]];
            double[] motion = [tempA.Motion.X, tempA.Motion.Y, tempA.Motion.Z, 0];

            Quaterniond.Multiply(motion, rot, motion);
            Quaterniond.Multiply(motion, motion, rotStar);
            
            tempA.Motion.X = motion[0] - tempB.Motion.X;
            tempA.Motion.Y = motion[1] - tempB.Motion.Y;
            tempA.Motion.Z = motion[2] - tempB.Motion.Z;
            return tempA.Motion;
        }

        public static bool AabbIntersect(Cuboidf aabb, double x, double y, double z, Cuboidf aabb2, Vec3d pos)
        {
            if (aabb2 == null) return true;

            return
                x + aabb.X1 < aabb2.X2 + pos.X &&
                x + aabb.X2 > aabb2.X1 + pos.X &&
                z + aabb.Z1 < aabb2.Z2 + pos.Z &&
                z + aabb.Z2 > aabb2.Z1 + pos.Z &&
                y + aabb.Y1 < aabb2.Y2 + pos.Y &&
                y + aabb.Y2 > aabb2.Y1 + pos.Y
                ;
        }

        public static EnumIntersect AabbIntersect(Cuboidd aabb, Cuboidd aabb2, Vec3d motion)
        {
            if (aabb.Intersects(aabb2)) return EnumIntersect.Stuck;

            // X
            if (
                aabb.X1 < aabb2.X2 + motion.X &&
                aabb.X2 > aabb2.X1 + motion.X &&
                aabb.Z1 < aabb2.Z2 &&
                aabb.Z2 > aabb2.Z1 &&
                aabb.Y1 < aabb2.Y2 &&
                aabb.Y2 > aabb2.Y1
            ) return EnumIntersect.IntersectX;

            // Y
            if (
                aabb.X1 < aabb2.X2 &&
                aabb.X2 > aabb2.X1 &&
                aabb.Z1 < aabb2.Z2 &&
                aabb.Z2 > aabb2.Z1 &&
                aabb.Y1 < aabb2.Y2 + motion.Y &&
                aabb.Y2 > aabb2.Y1 + motion.Y
            ) return EnumIntersect.IntersectY;

            // Z
            if (
                aabb.X1 < aabb2.X2 &&
                aabb.X2 > aabb2.X1 &&
                aabb.Z1 < aabb2.Z2 + motion.Z &&
                aabb.Z2 > aabb2.Z1 + motion.Z &&
                aabb.Y1 < aabb2.Y2 &&
                aabb.Y2 > aabb2.Y1
            ) return EnumIntersect.IntersectZ;

            return EnumIntersect.NoIntersect;
        }

    }


    /// <summary>
    /// Originally intended to be a special version of CollisionTester for BehaviorControlledPhysics, which does not re-do the WalkBlocks() call and re-generate the CollisionBoxList more than once in the same entity tick
    /// <br/>Currently in 1.20 the caching is not very useful when we loop through all entities sequentially - but empirical testing shows it is actually faster not to cache
    /// </summary>
    public class CachingVehicleCollisionTester : VehicleCollisionTester
    {
        public void NewTick(EntityPos entityPos)
        {
            minPos.Set(int.MinValue, int.MinValue, int.MinValue); 
            minPos.SetDimension(entityPos.Dimension);
            tmpPos.SetDimension(entityPos.Dimension);
        }

        public void AssignToEntity(PhysicsBehaviorBaseVehicle entityPhysics, int dimension)
        {
            minPos.SetDimension(dimension);
            tmpPos.SetDimension(dimension);
        }

        protected override void GenerateCollisionBoxList(IBlockAccessor blockAccessor, double motionX, double motionY, double motionZ, float stepHeight, float yExtra, int dimension)
        {
            PsuedoCuboidd sudoBox = this.sudoBox;  // Local reference for efficiency

            bool minPosIsUnchanged = minPos.SetAndEquals(
                (int)(sudoBox.X1 + Math.Min(0, motionX)),
                (int)(sudoBox.Y1 + Math.Min(0, motionY) - yExtra), // yExtra looks at blocks below to allow for the extra high collision box of fences
                (int)(sudoBox.Z1 + Math.Min(0, motionZ))
            );

            double y2 = Math.Max(sudoBox.Y1 + stepHeight, sudoBox.Y2);

            bool maxPosIsUnchanged = maxPos.SetAndEquals(
                (int)(sudoBox.X2 + Math.Max(0, motionX)),
                (int)(y2 + Math.Max(0, motionY)),
                (int)(sudoBox.Z2 + Math.Max(0, motionZ))
            );

            if (minPosIsUnchanged && maxPosIsUnchanged)
            {
                return;
            }

            CollisionBoxList2.Clear();
            blockAccessor.WalkBlocks(minPos, maxPos, (block, x, y, z) => {
                PsuedoCuboidd[] collisionBoxes = PsuedoCuboidd.CollectCuboidf(block.GetCollisionBoxes(blockAccessor, tmpPos.Set(x, y, z)), PsuedoCuboidd.Identity);
                if (collisionBoxes != null)
                {
                    CollisionBoxList2.Add(collisionBoxes, x, y, z, block);
                }
            }, true);
        }

        public void PushOutFromBlocks(IBlockAccessor blockAccessor, Entity entity, Vec3d tmpVec, float clippingLimit)
        {
            if (IsColliding(blockAccessor, entity.CollisionBox, tmpVec, false))
            {
                entity.Api.Logger.Event("Is Colliding");
                Vec3d pos = entity.Pos.XYZ;
                entityBox.SetAndTranslate(entity.CollisionBox, pos.X, pos.Y, pos.Z);

                GenerateCollisionBoxList(blockAccessor, 0, 0, 0, 0.5f, 0, entity.Pos.Dimension);

                int collisionBoxListCount = CollisionBoxList2.Count;
                if (collisionBoxListCount == 0) return;
                PsuedoCuboidd[] CollisionBoxListCuboids = CollisionBoxList2.cuboids;   // Local reference for efficiency

                double deltaX = 0;
                double deltaZ = 0;
                EnumPushDirection pushDirection = EnumPushDirection.None;
                var reducedBox = entity.CollisionBox.ToDouble();
                reducedBox.Translate(pos.X, pos.Y, pos.Z);
                reducedBox.GrowBy(-clippingLimit, 0, -clippingLimit);

                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    deltaX = CollisionBoxListCuboids[i].pushOutX(reducedBox, clippingLimit, ref pushDirection);
                }
                if (deltaX == clippingLimit)
                {
                    for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                    {
                        if (i >= collisionBoxListCount) break;
                        deltaX = CollisionBoxListCuboids[i].pushOutX(reducedBox, -clippingLimit, ref pushDirection);
                    }
                    deltaX += clippingLimit;
                }
                else deltaX -= clippingLimit;

                for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                {
                    if (i >= collisionBoxListCount) break;
                    deltaZ = CollisionBoxListCuboids[i].pushOutZ(reducedBox, clippingLimit, ref pushDirection);
                }
                if (deltaZ == clippingLimit)
                {
                    for (int i = 0; i < CollisionBoxListCuboids.Length; i++)
                    {
                        if (i >= collisionBoxListCount) break;
                        deltaZ = CollisionBoxListCuboids[i].pushOutZ(reducedBox, -clippingLimit, ref pushDirection);
                    }
                    deltaZ += clippingLimit;
                }
                else deltaZ -= clippingLimit;

                entity.Pos.X = pos.X + deltaX;
                entity.Pos.Z = pos.Z + deltaZ;
            }
        }
    }
}