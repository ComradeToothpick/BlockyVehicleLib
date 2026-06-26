// using System;
// using HarmonyLib;
// using Vintagestory.API.Common;
// using Vintagestory.API.MathTools;
// using Vintagestory.Common;
//
// namespace BlockyVehicleLib.Patches
// {
//     [HarmonyPatch(typeof(BlockAccessorBase), nameof(BlockAccessorBase.WalkBlocks))]
//     public static class BlockAccessorBase_WalkBlocks_Patch
//     {
//         internal static readonly WorldMap worldmap;
//
//         [HarmonyPrefix]
//         public static bool Prefix(BlockAccessorBase __instance,
//             BlockPos minPos,
//             BlockPos maxPos,
//             Action<Block, int, int, int> onBlock,
//             bool centerOrder = false)
//         {
//             int mapSizeX = worldmap.MapSizeX;
//             int ClampedMinX = GameMath.Clamp(Math.Min(minPos.X, maxPos.X), 0, mapSizeX);
//             int ClampedMaxX = GameMath.Clamp(Math.Max(minPos.X, maxPos.X), 0, mapSizeX);
//             int mapSizeY = worldmap.MapSizeY;
//             int ClampedMinY = GameMath.Clamp(Math.Min(minPos.Y, maxPos.Y), 0, mapSizeY);
//             int ClampedMaxY = GameMath.Clamp(Math.Max(minPos.Y, maxPos.Y), 0, mapSizeY);
//             if (minPos.dimension == 1)
//             {
//                 ClampedMinY = Math.Min(minPos.Y, maxPos.Y); //Do not clamp for MiniDimensions
//                 ClampedMaxY = Math.Max(minPos.Y, maxPos.Y);
//             }
//
//             int mapSizeZ = worldmap.MapSizeZ;
//             int ClampedMinZ = GameMath.Clamp(Math.Min(minPos.Z, maxPos.Z), 0, mapSizeZ);
//             int ClampedMaxZ = GameMath.Clamp(Math.Max(minPos.Z, maxPos.Z), 0, mapSizeZ);
//             int mincx = ClampedMinX / 32 /*0x20*/;
//             int mincy = ClampedMinY / 32 /*0x20*/;
//             int mincz = ClampedMinZ / 32 /*0x20*/;
//             int maxcx = ClampedMaxX / 32 /*0x20*/;
//             int maxcy = ClampedMaxY / 32 /*0x20*/;
//             int maxcz = ClampedMaxZ / 32 /*0x20*/;
//             int dimensionOffset = minPos.dimension * 1024 /*0x0400*/;
//             if (minPos.dimension == 1)
//             {
//                 dimensionOffset = 0; //MiniDimension Height coordinates should be interpreted at their given value
//             }
//
//             ChunkData[] cache = LoadChunksToCache(mincx, mincy + dimensionOffset, mincz, maxcx, maxcy + dimensionOffset,
//                 maxcz, (Action<int, int, int>)null);
//             int Length = maxcx - mincx + 1;
//             int Width = maxcz - mincz + 1;
//             if (centerOrder)
//             {
//                 int ClampedLength = ClampedMaxX - ClampedMinX;
//                 int ClampedHeight = ClampedMaxY - ClampedMinY;
//                 int ClampedWidth = ClampedMaxZ - ClampedMinZ;
//                 int halfExtentX = ClampedLength / 2;
//                 int halfExtentY = ClampedHeight / 2;
//                 int halfExtentZ = ClampedWidth / 2;
//                 for (int i = 0; i <= ClampedLength; ++i)
//                 {
//                     int oddDetectori = i & 1;
//                     int blockX = halfExtentX - (1 - oddDetectori * 2) * (i + oddDetectori) / 2 + ClampedMinX;
//                     int chunkX = blockX / 32 /*0x20*/ - mincx;
//                     for (int j = 0; j <= ClampedHeight; ++j)
//                     {
//                         int oddDetectorj = j & 1;
//                         int blockY = halfExtentY - (1 - oddDetectorj * 2) * (j + oddDetectorj) / 2 + ClampedMinY;
//                         int AreaXY = blockY % 32 /*0x20*/ * 32 /*0x20*/ * 32 /*0x20*/ + blockX % 32 /*0x20*/;
//                         int AreaZY = (blockY / 32 /*0x20*/ - mincy) * Width - mincz;
//                         for (int k = 0; k <= ClampedWidth; ++k)
//                         {
//                             int oddDetectork = k & 1;
//                             int blockZ = halfExtentZ - (1 - oddDetectork * 2) * (k + oddDetectork) / 2 + ClampedMinZ;
//                             ChunkData chunkData = cache[(AreaZY + blockZ / 32 /*0x20*/) * Length + chunkX];
//                             if (chunkData != null)
//                             {
//                                 int index3d = AreaXY + blockZ % 32 /*0x20*/ * 32 /*0x20*/;
//                                 int fluid = chunkData.GetFluid(index3d);
//                                 if (fluid != 0)
//                                     onBlock(worldmap.Blocks[fluid], blockX, blockY, blockZ);
//                                 int solidBlock = chunkData.GetSolidBlock(index3d);
//                                 onBlock(worldmap.Blocks[solidBlock], blockX, blockY, blockZ);
//                             }
//                         }
//                     }
//                 }
//             }
//             else
//             {
//                 for (int y = ClampedMinY; y <= ClampedMaxY; ++y)
//                 {
//                     int chunkYZ = (y / 32 /*0x20*/ - mincy) * Width - mincz;
//                     for (int z = ClampedMinZ; z <= ClampedMaxZ; ++z)
//                     {
//                         int chunkXYZ = (chunkYZ + z / 32 /*0x20*/) * Length - mincx;
//                         int blockXYZ = (y % 32 /*0x20*/ * 32 /*0x20*/ + z % 32 /*0x20*/) * 32 /*0x20*/;
//                         for (int x = ClampedMinX; x <= ClampedMaxX; ++x)
//                         {
//                             ChunkData chunkData = cache[chunkXYZ + x / 32 /*0x20*/];
//                             if (chunkData != null)
//                             {
//                                 int index3d = blockXYZ + x % 32 /*0x20*/;
//                                 int fluid = chunkData.GetFluid(index3d);
//                                 if (fluid != 0)
//                                     onBlock(worldmap.Blocks[fluid], x, y, z);
//                                 int solidBlock = chunkData.GetSolidBlock(index3d);
//                                 onBlock(worldmap.Blocks[solidBlock], x, y, z);
//                             }
//                         }
//                     }
//                 }
//             }
//
//             return false;
//         }
//         public static ChunkData[] LoadChunksToCache(
//             int mincx,
//             int mincy,
//             int mincz,
//             int maxcx,
//             int maxcy,
//             int maxcz,
//             Action<int, int, int> onChunkMissing)
//         {
//             int Width = maxcx - mincx + 1;
//             int Height = maxcy - mincy + 1;
//             int Length = maxcz - mincz + 1;
//             ChunkData[] cache = new ChunkData[Width * Height * Length];
//             for (int chunkY = mincy; chunkY <= maxcy; ++chunkY)
//             {
//                 int num4 = (chunkY - mincy) * Length - mincz;
//                 for (int chunkZ = mincz; chunkZ <= maxcz; ++chunkZ)
//                 {
//                     int num5 = (num4 + chunkZ) * Width - mincx;
//                     for (int chunkX = mincx; chunkX <= maxcx; ++chunkX)
//                     {
//                         IWorldChunk chunk = worldmap.GetChunk(chunkX, chunkY, chunkZ);
//                         if (chunk == null)
//                         {
//                             cache[num5 + chunkX] = (ChunkData) null;
//                             if (onChunkMissing != null)
//                                 onChunkMissing(chunkX, chunkY, chunkZ);
//                         }
//                         else
//                         {
//                             chunk.Unpack();
//                             cache[num5 + chunkX] = chunk.Data as ChunkData;
//                         }
//                     }
//                 }
//             }
//             return cache;
//         }
//     }
// }