using UnityEngine;
using System.Runtime.CompilerServices;
using System;
using Unity.Collections;

public class VoxelChunk : IDisposable
{
    public const int ChunkSize = 32;
    public const int ChunkSlice = ChunkSize * ChunkSize;
    public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;

    public const int VoxelsToMeter = 16;

    [NonSerialized]
    private NativeArray<Voxel> voxels;

    public Vector3Int coordinate;

    [NonSerialized]
    public Action<VoxelChunk> chunkModified;

    public Voxel this[int x, int y, int z]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            return GetVoxel(x, y, z);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            SetVoxel(x, y, z, value);
        }
    }

    public VoxelChunk(Vector3Int coordinate)
    {
        this.coordinate = coordinate;
        this.voxels = new(ChunkVolume, Allocator.Persistent, NativeArrayOptions.ClearMemory);
    }

    public VoxelChunk(Vector3Int coordinate, Voxel[] voxels)
    {
        this.coordinate = coordinate;
        this.voxels = new(voxels, Allocator.Persistent);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NativeArray<Voxel> GetNativeArray()
    {
        return voxels;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetVoxelIndex(Vector3Int coord)
    {
        return coord.x + (coord.y * ChunkSize) + (coord.z * ChunkSlice);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel GetVoxel(Vector3Int coord)
    {
        if (coord.x < 0 || coord.x >= ChunkSize ||
            coord.y < 0 || coord.y >= ChunkSize ||
            coord.z < 0 || coord.z >= ChunkSize)
        {
            return new Voxel();
        }
        
        return voxels[GetVoxelIndex(coord)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel GetVoxel(int x, int y, int z)
    {
        return GetVoxel(new Vector3Int(x, y, z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, Voxel voxel)
    {
        if (coord.x < 0 || coord.x >= ChunkSize ||
            coord.y < 0 || coord.y >= ChunkSize ||
            coord.z < 0 || coord.z >= ChunkSize)
        {
            return;
        }
        
        voxels[GetVoxelIndex(coord)] = voxel;
        chunkModified?.Invoke(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        SetVoxel(new Vector3Int(x, y, z), voxel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, Color color)
    {
        SetVoxel(new Vector3Int(x, y, z), new Voxel(color));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, Color color)
    {
        SetVoxel(coord, new Voxel(color));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, Color32 color)
    {
        SetVoxel(new Vector3Int(x, y, z), new Voxel(color));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, Color32 color)
    {
        SetVoxel(coord, new Voxel(color));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, byte r, byte g, byte b, byte a)
    {
        SetVoxel(new Vector3Int(x, y, z), new Voxel(new Color32(r, g, b, a)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, byte r, byte g, byte b, byte a)
    {
        SetVoxel(coord, new Voxel(new Color32(r, g, b, a)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, float r, float g, float b, float a)
    {
        SetVoxel(new Vector3Int(x, y, z), new Voxel(new Color(r, g, b, a)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, float r, float g, float b, float a)
    {
        SetVoxel(coord, new Voxel(new Color(r, g, b, a)));
    }

    public void Dispose()
    {
        voxels.Dispose();
    }
}
