using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace BlockyVehicleLib.Util
{ 
    public class PsuedoCuboidd
    {
        //const double epsilon = 0.000016;
        //const double pi = Math.PI;
        public Vec3d pos { get; set; }//position of the centroid
        public Vec3d size { get; set; }//length, width, height
        public double[] rotation;//Quaterniond rotation
        public static Vec3d localOrigin = new Vec3d(0, 0, 0);
        public Vec3d[] internalCorners;
        public static double[] Identity = Quaterniond.FromValues(0.0, 0.0, 0.0, 1.0);
        public Vec3d[] externalCorners;
        public int highestX, highestY, highestZ, lowestX, lowestY, lowestZ;//integer that points to a Vec3d in externalCorners
        public double X1 => externalCorners[lowestX].X;
        public double X2 => externalCorners[highestX].X;
        public double Y1 => externalCorners[lowestY].Y;
        public double Y2 => externalCorners[highestY].Y;
        public double Z1 => externalCorners[lowestZ].Z;
        public double Z2 => externalCorners[highestZ].Z;
        public bool externalCornersDirty = false;
        public EntityChunky parentEntity;
        public PsuedoCuboidd? parentCuboid = null;
        public PsuedoCuboidd[]? childrenCuboids = null;

        public PsuedoCuboidd()
        {
            this.pos = new Vec3d(0, 0, 0);
            this.size = new Vec3d(1, 1, 1);
            this.rotation = Identity;
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }
        
        public PsuedoCuboidd(Vec3d pos, Vec3d size, double[] rotation)
        {
            this.pos = pos;
            this.size = size;
            this.rotation = rotation;
            
            
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }

        public PsuedoCuboidd(EntityChunky parentEntity, Vec3d localPos, Vec3d localSize)
        {
            this.parentEntity = parentEntity;
            this.pos = new Vec3d(parentEntity.Pos) + localPos;
            this.size = localSize;
            this.rotation = ConvertEulerAngles(parentEntity.Pos.Pitch, parentEntity.Pos.Yaw, parentEntity.Pos.Roll);
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }

        public PsuedoCuboidd(PsuedoCuboidd parentCuboid, Vec3d localPos, Vec3d localSize)
        {
            this.parentCuboid = parentCuboid;
            this.pos = parentCuboid.pos + localPos;
            this.size = localSize;
            this.rotation = parentCuboid.rotation;
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }
        
        public PsuedoCuboidd(Vec3d pos, Vec3d size)
        {
            this.pos = pos;
            this.size = size;
            this.rotation = Identity;
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }

        public PsuedoCuboidd(double x, double y, double z)
        {
            this.pos = new Vec3d(x, y, z);
            this.size = new Vec3d(1, 1, 1);
            this.rotation = Identity;
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }

        public PsuedoCuboidd(Cuboidf cuboid, double[] rotation)
        {
            this.size.X = cuboid.Width;
            this.size.Y = cuboid.Height;
            this.size.Z = cuboid.Length;
            this.pos = new Vec3d(cuboid.X2 - cuboid.Width/2, cuboid.Y2 - cuboid.Height/2, cuboid.Z2 - cuboid.Length/2);
            this.SetRotation(rotation);
            internalCorners = new Vec3d[8];
            externalCorners = new Vec3d[8];
            SetInternalCorners();
            GetExternalCorners();
        }

        public static PsuedoCuboidd[] CollectCuboidf(Cuboidf[] cuboids, double[] rotation)
        {
            PsuedoCuboidd[] output = new PsuedoCuboidd[cuboids.Length];
            for (int i = 0; i < cuboids.Length; i++)
            {
                output[i] = new PsuedoCuboidd(cuboids[i], rotation);
            }
            return output;
        }

        public PsuedoCuboidd SetFromCuboidf(Cuboidf cuboid, Vec3d pos)
        {
            this.pos = pos;
            this.SetRotation(Identity);
            this.size.X = cuboid.Length;
            this.size.Y = cuboid.Height;
            this.size.Z = cuboid.Width;
            SetInternalCorners();
            GetExternalCorners();
            return this;
        }
        
        public PsuedoCuboidd SetFromCuboidf(Cuboidf cuboid, EntityPos pos)
        {
            this.pos = pos.XYZ;
            this.SetRotation(ConvertEulerAngles(pos.Pitch, pos.Yaw, pos.Roll));
            this.size.X = cuboid.Length;
            this.size.Y = cuboid.Height;
            this.size.Z = cuboid.Width;
            SetInternalCorners();
            GetExternalCorners();
            return this;
        }
        
        public PsuedoCuboidd SetFromEntityPos(EntityPos entityPos)
        {
            this.pos = entityPos.XYZ;
            this.rotation = ConvertEulerAngles(entityPos.Pitch, entityPos.Yaw, entityPos.Roll);
            this.externalCornersDirty = true;
            SetInternalCorners();
            GetExternalCorners();
            return this;
        }

        
        /*
        w = c1 c2 c3 - s1 s2 s3
        x = s1 s2 c3 +c1 c2 s3
        y = s1 c2 c3 + c1 s2 s3
        z = c1 s2 c3 - s1 c2 s3
        
        Where:
        c1 = cos(yaw / 2)
        c2 = cos(pitch / 2)
        c3 = cos(roll / 2)
        s1 = sin(yaw / 2)
        s2 = sin(pitch / 2)
        s3 = sin(roll / 2)
        */
        public static double[] ConvertEulerAngles(double pitch, double yaw, double roll)
        {
            double[] output = new double[4];
            output[0] = Math.Sin(yaw / 2) * Math.Sin(pitch / 2) * Math.Cos(roll / 2) +
                        Math.Cos(yaw / 2) * Math.Cos(pitch / 2) * Math.Sin(roll / 2);
            output[1] = Math.Sin(yaw / 2) * Math.Cos(pitch / 2) * Math.Cos(roll / 2) +
                        Math.Cos(yaw / 2) * Math.Sin(pitch / 2) * Math.Sin(roll / 2);
            output[2] = Math.Cos(yaw / 2) * Math.Sin(pitch / 2) * Math.Cos(roll / 2) -
                        Math.Sin(yaw / 2) * Math.Cos(pitch / 2) * Math.Sin(roll / 2);
            output[3] = Math.Cos(yaw / 2) * Math.Cos(pitch / 2) * Math.Cos(roll / 2) - 
                        Math.Sin(yaw / 2) * Math.Sin(pitch / 2) * Math.Sin(roll / 2);
            return output;
        }
        
        public void SetInternalCorners()
        {
            internalCorners[0] = new Vec3d(size.X/2, size.Y/2, size.Z/2);
            internalCorners[1] = new Vec3d(-size.X/2, size.Y/2, size.Z/2);
            internalCorners[2] = new Vec3d(size.X/2, -size.Y/2, size.Z/2);
            internalCorners[3] = new Vec3d(-size.X/2, -size.Y/2, size.Z/2);
            internalCorners[4] = new Vec3d(size.X/2, size.Y/2, -size.Z/2);
            internalCorners[5] = new Vec3d(-size.X/2, size.Y/2, -size.Z/2);
            internalCorners[6] = new Vec3d(size.X/2, -size.Y/2, -size.Z/2);
            internalCorners[7] = new Vec3d(-size.X/2, -size.Y/2, -size.Z/2);
            externalCornersDirty = true;
        }
        
        /// <summary>
        /// This should only be used in place of a regular cuboidd.
        /// Sets the position and size of the cuboidd.
        /// </summary>
        /// <param name="x1"></param>
        /// <param name="y1"></param>
        /// <param name="z1"></param>
        /// <param name="x2"></param>
        /// <param name="y2"></param>
        /// <param name="z2"></param>
        /// <returns></returns>
        public PsuedoCuboidd Set(double x1, double y1, double z1, double x2, double y2, double z2)
        {
            this.pos = new Vec3d((x1 + x2)/2, (y1 + y2)/2, (z1 + z2)/2);
            this.size = new Vec3d(Math.Abs(x2 - x1), Math.Abs(y2 - y1), Math.Abs(z2 - z1));
            this.rotation = Identity;
            return this;
        }
        
        public Vec3d[] GetExternalCorners()
        {
            if (!externalCornersDirty) return externalCorners;
            double[] q = this.rotation;
            Quaterniond.Normalize(q, q);
            double[] q1 = Quaterniond.FromValues(-q[0], -q[1], -q[2], q[3]);
            double[] output = new double[4];
            for (int i = 0; i < 8; i++)
            {
                double[] localCoord = Quaterniond.FromValues(internalCorners[i].X, internalCorners[i].Y, internalCorners[i].Z, 0);
                
                Quaterniond.Multiply(output, q, localCoord);
                Quaterniond.Multiply(output, output, q1);
                externalCorners[i] = new Vec3d(output[0] + pos.X, output[1] + pos.Y, output[2] + pos.Z);
                if (externalCorners[i].X > X2) highestX = i;
                if (externalCorners[i].X < X1) lowestX = i;
                if (externalCorners[i].Y > Y2) highestY = i;
                if (externalCorners[i].Y < Y1) lowestY = i;
                if (externalCorners[i].Z > Z2) highestZ = i;
                if (externalCorners[i].Z < Z1) lowestZ = i;
            }

            externalCornersDirty = false;
            return externalCorners;
        }

        public void SetRotation(double[] rotation)
        {
            this.rotation = rotation;
            externalCornersDirty = true;
        }

        public void SetPosition(double x, double y, double z)
        {
            this.pos = new Vec3d(x, y, z);
            externalCornersDirty = true;
            GetExternalCorners();
        }

        public double[] ApplyRotation(double[] angVelocity, double[] rot, double dt)
        {
            double[] output = rot;
            double[] out2 = new double[4];
            Quaterniond.Multiply(out2, angVelocity, rot);
            Quaterniond.Scale(out2, out2, dt/2);
            Quaterniond.Add(output, output, out2);
            Quaterniond.Normalize(output, output);
            externalCornersDirty = true;
            return output;
        }

        public Vec3d ApplyTranslation(Vec3d momentum, double dt)
        {
            this.pos.X += momentum.X * dt;
            this.pos.Y += momentum.Y * dt;
            this.pos.Z += momentum.Z * dt;
            externalCornersDirty = true;
            return this.pos;
        }

        public void ScaleMult(Vec3d sizeScale)
        {
            this.size.X *= sizeScale.X;
            this.size.Y *= sizeScale.Y;
            this.size.Z *= sizeScale.Z;
            SetInternalCorners();
        }
        
        public void ScaleAdd(Vec3d sizeScale)
        {
            this.size.X += sizeScale.X;
            this.size.Y += sizeScale.Y;
            this.size.Z += sizeScale.Z;
            SetInternalCorners();
        }

        public PsuedoCuboidd OffsetCopyDouble(double x, double y, double z)
        {
            
            return new PsuedoCuboidd(new Vec3d(x, y, z), this.size, this.rotation);
        }
        
        public bool ContainsOrTouches(double x, double y, double z)
        {
            return x >= X1 
                   && x <= X2 
                   && y >= Y1 
                   && y <= Y2
                   && z >= Z1 
                   && z <= Z2;
        }
        
        public bool ContainsOrTouches(IVec3 vec)
        {
            return this.ContainsOrTouches(vec.XAsDouble, vec.YAsDouble, vec.ZAsDouble);
        }
        
        //coordinates should refer to the internal coordinates
        public PsuedoCuboidd GrowToInclude(double x, double y, double z)
        {
            //TODO: Make this dimensionally aware later
            //This should be used to grow the PsuedoCuboid in reference to the minidimension, and then projected into the main dimension
            //The coordinates should be relative to the minidimension
            //This is going to need to do the following:
            //find the position relative to the PsuedoCuboid
            //adjust the scale
            //recalculate the internal corners (marks the external corners as dirty)
            //adjust the position to reflect the change in centroid
            
            Vec3d coord = new Vec3d(x, y, z);
            Vec3d posMod = new Vec3d(0, 0, 0);
            if (this.size.X / 2 < coord.X)
            {
                this.size.X += (coord.X - this.size.X / 2);
                posMod.X = (coord.X - this.size.X / 2)/2;
            }

            if (-this.size.X / 2 > coord.X)
            {
                this.size.X += (this.size.X / 2 - coord.X);
                posMod.X = (coord.X - this.size.X / 2)/2;
            }

            if (this.size.Y / 2 < coord.Y)
            {
                this.size.Y += (coord.Y - this.size.Y / 2);
                posMod.Y = (coord.Y - this.size.Y / 2)/2;
            }

            if (-this.size.Y / 2 > coord.Y)
            {
                this.size.Y += (this.size.Y / 2 - coord.Y);
                posMod.Y = (coord.Y - this.size.Y / 2)/2;
            }

            if (this.size.Z / 2 < coord.Z)
            {
                this.size.Z += (coord.Z - this.size.Z / 2);
                posMod.Z = (coord.Z - this.size.Z / 2)/2;
            }

            if (-this.size.Z / 2 > coord.Z)
            {
                this.size.Z += (this.size.Z / 2 - coord.Z);
                posMod.Z = (coord.Z - this.size.Z / 2)/2;
            }
            
            //Quaternion time!
            double[] q = this.rotation;
            double[] q1 = Quaterniond.FromValues(-q[0], -q[1], -q[2], q[3]);
            double[] posModRot = new double[] {posMod.X, posMod.Y, posMod.Z, 0};
            Quaterniond.Normalize(posModRot, posModRot);
            
            
            Quaterniond.Multiply(posModRot, q, posModRot);
            Quaterniond.Multiply(posModRot, posModRot, q1);
            posMod.X *= posModRot[0];
            posMod.Y *= posModRot[1];
            posMod.Z *= posModRot[2];

            this.Translate(posMod);
            
            return this;
        }

        /// <summary>
        /// Redundant function
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="allEntityChunky"></param>
        /// <returns></returns>
        public static EntityChunky[]? FindNearbyVehicles(Entity entity, EntityChunky[] allEntityChunky)
        {
            List<EntityChunky> nearbyChunkies = new List<EntityChunky>();
            foreach (EntityChunky chunky in allEntityChunky)
            {
                if (chunky.Pos.DistanceTo(entity.Pos) <= entity.World.DefaultEntityTrackingRange)
                {
                    nearbyChunkies.Add(chunky);
                }
            }
            return nearbyChunkies.ToArray();
        }
        /// <summary>
        /// Redundant function
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="allEntityChunky"></param>
        /// <returns></returns>
        public static EntityChunky[]? FindNearbyVehicles(EntityPos entityPos, EntityChunky[] allEntityChunky)
        {
            List<EntityChunky> nearbyChunkies = new List<EntityChunky>();
            foreach (EntityChunky chunky in allEntityChunky)
            {
                if (chunky.Pos.DistanceTo(entityPos) <= GlobalConstants.DefaultSimulationRange)
                {
                    nearbyChunkies.Add(chunky);
                }
            }
            return nearbyChunkies.ToArray();
        }
        /// <summary>
        /// Redundant function
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="allEntityChunky"></param>
        /// <returns></returns>
        public static EntityChunky[]? FindNearbyVehicles(Vec3d pos, EntityChunky[] allEntityChunky)
        {
            List<EntityChunky> nearbyChunkies = new List<EntityChunky>();
            foreach (EntityChunky chunky in allEntityChunky)
            {
                if (chunky.Pos.DistanceTo(pos) <= GlobalConstants.DefaultSimulationRange)
                {
                    nearbyChunkies.Add(chunky);
                }
            }
            return nearbyChunkies.ToArray();
        }
        
        public bool Intersects(Cuboidd other)
        {
            return this.X2 > other.X1 && this.X1 < other.X2 && this.Y2 > other.Y1 && this.Y1 < other.Y2 && this.Z2 > other.Z1 && this.Z1 < other.Z2;
        }
        
        public bool Intersects(PsuedoCuboidd other)
        {
            return this.X2 > other.X1 && this.X1 < other.X2 && this.Y2 > other.Y1 && this.Y1 < other.Y2 && this.Z2 > other.Z1 && this.Z1 < other.Z2;
        }

        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool Intersects(Cuboidf other)
        {
            return this.X2 > (double) other.X1 && this.X1 < (double) other.X2 && this.Y2 > (double) other.Y1 && this.Y1 < (double) other.Y2 && this.Z2 > (double) other.Z1 && this.Z1 < (double) other.Z2;
        }

        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool Intersects(Cuboidf other, Vec3d offset)
        {
            return this.X2 > (double) other.X1 + offset.X && this.X1 < (double) other.X2 + offset.X && this.Z2 > (double) other.Z1 + offset.Z && this.Z1 < (double) other.Z2 + offset.Z && this.Y2 > (double) other.Y1 + offset.Y && this.Y1 < Math.Round((double) other.Y2 + offset.Y, 5);
        }
        
        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool Intersects(PsuedoCuboidd other, Vec3d offset)
        {
            return this.X2 > (double) other.X1 + offset.X && this.X1 < (double) other.X2 + offset.X && this.Z2 > (double) other.Z1 + offset.Z && this.Z1 < (double) other.Z2 + offset.Z && this.Y2 > (double) other.Y1 + offset.Y && this.Y1 < Math.Round((double) other.Y2 + offset.Y, 5);
        }

        public bool Intersects(Cuboidf other, double offsetx, double offsety, double offsetz)
        {
            return this.X2 > (double) other.X1 + offsetx && this.X1 < (double) other.X2 + offsetx && this.Z2 > (double) other.Z1 + offsetz && this.Z1 < (double) other.Z2 + offsetz && this.Y2 > (double) other.Y1 + offsety && this.Y1 < Math.Round((double) other.Y2 + offsety, 5);
        }

        public bool IntersectsOrTouches(PsuedoCuboidd other, Vec3d offset)
        {
            return this.X2 >= (double) other.X1 + offset.X && this.X1 <= (double) other.X2 + offset.X && this.Z2 >= (double) other.Z1 + offset.Z && this.Z1 <= (double) other.Z2 + offset.Z && this.Y2 >= (double) other.Y1 + offset.Y && this.Y1 <= Math.Round((double) other.Y2 + offset.Y, 5);
        }
        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool IntersectsOrTouches(Cuboidd other)
        {
            return this.X2 >= other.X1 && this.X1 <= other.X2 && this.Y2 >= other.Y1 && this.Y1 <= other.Y2 && this.Z2 >= other.Z1 && this.Z1 <= other.Z2;
        }
        
        public bool IntersectsOrTouches(PsuedoCuboidd other)
        {
            return this.X2 >= other.X1 && this.X1 <= other.X2 && this.Y2 >= other.Y1 && this.Y1 <= other.Y2 && this.Z2 >= other.Z1 && this.Z1 <= other.Z2;
        }

        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool IntersectsOrTouches(Cuboidf other, Vec3d offset)
        {
            return this.X2 >= (double) other.X1 + offset.X && this.X1 <= (double) other.X2 + offset.X && this.Z2 >= (double) other.Z1 + offset.Z && this.Z1 <= (double) other.Z2 + offset.Z && this.Y2 >= (double) other.Y1 + offset.Y && this.Y1 <= Math.Round((double) other.Y2 + offset.Y, 5);
        }

        /// <summary>If the given cuboid intersects with this cuboid</summary>
        public bool IntersectsOrTouches(Cuboidf other, double offsetX, double offsetY, double offsetZ)
        {
            return (this.X2 < (double) other.X1 + offsetX || this.X1 > (double) other.X2 + offsetX || this.Y2 < (double) other.Y1 + offsetY || this.Y1 > (double) other.Y2 + offsetY || this.Z2 < (double) other.Z1 + offsetZ ? 1 : (this.Z1 > (double) other.Z2 + offsetZ ? 1 : 0)) == 0;
        }
        
        public PsuedoCuboidd GrowToInclude(IVec3 vec)
        {
            this.GrowToInclude(vec.XAsDouble, vec.YAsDouble, vec.ZAsDouble);
            return this;
        }
        
        public void RemoveRoundingErrors()
        {
            double a1 = this.pos.X * 16.0;
            double a2 = this.pos.Z * 16.0;
            //double d1 = this.X2 * 16.0;
            //double d2 = this.Z2 * 16.0;
            if (Math.Ceiling(a1) - a1 < 1.6E-05)
                this.pos.X = Math.Ceiling(a1) / 16.0;
            if (Math.Ceiling(a2) - a2 < 1.6E-05)
                this.pos.Z = Math.Ceiling(a2) / 16.0;
            /*
            if (d1 - Math.Floor(d1) < 1.6E-05)
                this.X2 = Math.Floor(d1) / 16.0;
            if (d2 - Math.Floor(d2) >= 1.6E-05)
                return;
            this.Z2 = Math.Floor(d2) / 16.0;
            */
        }
        
        public PsuedoCuboidd SetAndTranslate(PsuedoCuboidd selectionBox, Vec3d vec)
        {
            this.pos =  selectionBox.pos + vec;
            return this;
        }
        
        /// <summary>
        /// Returns a new x coordinate that's ensured to be outside this cuboid. Used for collision detection.
        /// </summary>
        public double pushOutX(PsuedoCuboidd from, double motx, ref EnumPushDirection direction)
        {
            //I'll need to completely redo these functions as the Cuboidd version this is based on could assume the cuboid was aligned with the grid
            //this might just draw a cube aligned with the grid around the cuboid
            direction = EnumPushDirection.None;
            if (from.externalCornersDirty) from.GetExternalCorners();
            if (from.Z2 > this.Z1 && from.Z1 < this.Z2 && from.Y2 > this.Y1 && from.Y1 < this.Y2)
            {
                if (motx > 0.0 && from.X2 <= this.X1 && this.X1 - from.X2 < motx)
                {
                    direction = EnumPushDirection.Positive;
                    motx = this.X1 - from.X2;
                }
                else if (motx < 0.0 && from.X1 >= this.X2 && this.X2 - from.X1 > motx)
                {
                    direction = EnumPushDirection.Negative;
                    motx = this.X2 - from.X1;
                }
            }
            return motx;
        }
        
        public double pushOutX(Cuboidd from, double motx, ref EnumPushDirection direction)
        {
            //I suspect I'll need to completely redo these functions
            //Define points in local coordinates just outside the cuboid
            
            direction = EnumPushDirection.None;
            
            if (from.Z2 > this.Z1 && from.Z1 < this.Z2 && from.Y2 > this.Y1 && from.Y1 < this.Y2)
            {
                if (motx > 0.0 && from.X2 <= this.X1 && this.X1 - from.X2 < motx)
                {
                    direction = EnumPushDirection.Positive;
                    motx = this.X1 - from.X2;
                }
                else if (motx < 0.0 && from.X1 >= this.X2 && this.X2 - from.X1 > motx)
                {
                    direction = EnumPushDirection.Negative;
                    motx = this.X2 - from.X1;
                }
            }
            return motx;
        }

        /// <summary>
        /// Returns a new y coordinate that's ensured to be outside this cuboid. Used for collision detection.
        /// </summary>
        public double pushOutY(PsuedoCuboidd from, double moty, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            if (from.X2 > this.X1 && from.X1 < this.X2 && from.Z2 > this.Z1 && from.Z1 < this.Z2)
            {
                if (moty > 0.0 && from.Y2 <= this.Y1 && this.Y1 - from.Y2 < moty)
                {
                    direction = EnumPushDirection.Positive;
                    moty = this.Y1 - from.Y2;
                }
                else if (moty < 0.0 && from.Y1 >= this.Y2 && this.Y2 - from.Y1 > moty)
                {
                    direction = EnumPushDirection.Negative;
                    moty = this.Y2 - from.Y1;
                }
            }
            return moty;
        }
        
        public double pushOutY(Cuboidd from, double moty, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            double thisX1 = this.externalCorners[lowestX].X;
            double thisX2 = this.externalCorners[highestX].X;
            double thisY1 = this.externalCorners[lowestY].Y;
            double thisY2 = this.externalCorners[highestY].Y;
            double thisZ1 = this.externalCorners[lowestZ].Z;
            double thisZ2 = this.externalCorners[highestZ].Z;
            if (from.X2 > thisX1 && from.X1 < thisX2 && from.Z2 > thisZ1 && from.Z1 < thisZ2)
            {
                if (moty > 0.0 && from.Y2 <= thisY1 && thisY1 - from.Y2 < moty)
                {
                    direction = EnumPushDirection.Positive;
                    moty = thisY1 - from.Y2;
                }
                else if (moty < 0.0 && from.Y1 >= thisY2 && thisY2 - from.Y1 > moty)
                {
                    direction = EnumPushDirection.Negative;
                    moty = thisY2 - from.Y1;
                }
            }
            return moty;
        }

        /// <summary>
        /// Returns a new z coordinate that's ensured to be outside this cuboid. Used for collision detection.
        /// </summary>
        public double pushOutZ(PsuedoCuboidd from, double motz, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            if (from.X2 > this.X1 && from.X1 < this.X2 && from.Y2 > this.Y1 && from.Y1 < this.Y2)
            {
                if (motz > 0.0 && from.Z2 <= this.Z1 && this.Z1 - from.Z2 < motz)
                {
                    direction = EnumPushDirection.Positive;
                    motz = this.Z1 - from.Z2;
                }
                else if (motz < 0.0 && from.Z1 >= this.Z2 && this.Z2 - from.Z1 > motz)
                {
                    direction = EnumPushDirection.Negative;
                    motz = this.Z2 - from.Z1;
                }
            }
            return motz;
        }
        
        public double pushOutZ(Cuboidd from, double motz, ref EnumPushDirection direction)
        {
            direction = EnumPushDirection.None;
            if (from.X2 > this.X1 && from.X1 < this.X2 && from.Y2 > this.Y1 && from.Y1 < this.Y2)
            {
                if (motz > 0.0 && from.Z2 <= this.Z1 && this.Z1 - from.Z2 < motz)
                {
                    direction = EnumPushDirection.Positive;
                    motz = this.Z1 - from.Z2;
                }
                else if (motz < 0.0 && from.Z1 >= this.Z2 && this.Z2 - from.Z1 > motz)
                {
                    direction = EnumPushDirection.Negative;
                    motz = this.Z2 - from.Z1;
                }
            }
            return motz;
        }
        
        /// <summary>Adds the given offset to the cuboid</summary>
        public PsuedoCuboidd Translate(IVec3 vec)
        {
            this.pos += new Vec3d(vec.XAsDouble, vec.YAsDouble, vec.ZAsDouble);
            externalCornersDirty = true;
            return this;
        }

        /// <summary>Adds the given offset to the cuboid</summary>
        public PsuedoCuboidd Translate(double posX, double posY, double posZ)
        {
            return this.Translate(new Vec3d(posX, posY, posZ));
        }

        public float[] GetEulerAngles()
        {
            return Quaterniond.ToEulerAngles(this.rotation);
        }

        public PsuedoCuboidd? GetParentCuboid()
        {
            return this.parentCuboid;
        }

        public PsuedoCuboidd GetFirstAncestor()
        {
            PsuedoCuboidd? latestParent = this.parentCuboid;
            while (latestParent != null)
            {
                if (latestParent.GetParentCuboid() == null) return latestParent;
                latestParent = latestParent.GetParentCuboid();
            }
            return this;
        }

        public PsuedoCuboidd[]? GetChildren()
        {
            return this.childrenCuboids;
        }

        /// <summary>
        /// Depth first search to find all descendants of the given cuboid.
        /// </summary>
        /// <param name="cuboid"></param>
        /// <param name="descendants"></param>
        /// <param name="depth"></param>
        /// <returns></returns>
        public static PsuedoCuboidd[] GetAllDescendants(PsuedoCuboidd cuboid, ref List<PsuedoCuboidd> descendants, int depth = 0)
        {
            if(depth != 0) descendants.Add(cuboid);
            if (cuboid.childrenCuboids != null)
            {
                depth++;
                for (int i = 0; i < cuboid.childrenCuboids.Length; i++)
                {
                    PsuedoCuboidd.GetAllDescendants(cuboid.childrenCuboids[i], ref descendants, depth);
                }
            }
            return descendants.ToArray();
        }

        public void CreateChild(Vec3d localPos, Vec3d localSize)
        {
            PsuedoCuboidd newChild = new PsuedoCuboidd(this, localPos, localSize);
            PsuedoCuboidd[] newChildArray;
            if (this.childrenCuboids == null) newChildArray = new PsuedoCuboidd[1];
            else
            {
                newChildArray = new PsuedoCuboidd[this.childrenCuboids.Length + 1];
            }

            newChildArray[-1] = newChild;
        }
    }
    /*
    public class PsuedoCuboidf : Cuboidf
    {
        public Vec3f pos { get; set; }//position of the centroid
        public Vec3f size { get; set; }//length, width, height
        public double[] rotation { get; set; }//Quaternion
        public PsuedoCuboidf(Vec3f pos, Vec3f size, double[] rotation)
        {
            this.pos = pos;
            this.size = size;
            this.rotation = rotation;
        }
    }
    */
}