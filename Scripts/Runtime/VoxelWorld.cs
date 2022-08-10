using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;

[ExecuteAlways]
public class VoxelWorld : MonoBehaviour
{
    [NonSerialized]
    public Dictionary<Vector3Int, VoxelChunk> chunks = new();

    public VoxelWorldObject worldAsset;

    [NonSerialized]
    public Action<Vector3Int, Mesh> chunkUpdated;

    [NonSerialized]
    public Action<Vector3Int> chunkDeleted;
    
    private void OnEnable()
    {
        LoadVoxelWorldAsset(worldAsset);
    }

    private void OnDisable()
    {
        Clear();
    }

    // TODO: Considering how small chunks are
    // (One cubic meter, think of it as, one voxel = one Minecraft pixel on a texture),
    // it may actually be better for performance to split worlds up into
    // hashtable'd sections of multiple chunks and just allocate all of them
    // when we need that section (Obviously less memory efficient, but could be better if we're often modifying chunks).

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

    /*
     * Attempts to get the chunk at the given coordinates, returns null if the chunk is empty.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoxelChunk GetChunk(Vector3Int coord)
    {
        if (chunks.TryGetValue(coord, out VoxelChunk chunk))
            return chunk;
        return null;
    }

    /*
     * Attempts to get the chunk at the given coordinates, if it doesn't exist it creates an empty one and returns it.
     */
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoxelChunk GetChunkOrCreate(Vector3Int coord)
    {
        if (!chunks.TryGetValue(coord, out VoxelChunk chunk))
        {
            chunk = new VoxelChunk(coord);
            AddChunk(chunk);
        }
        return chunk;
    }

    public void LoadVoxelWorldAsset(VoxelWorldObject asset)
    {
        Clear();
        
        foreach (SerializableChunk chunk in asset.GetChunks())
        {
            VoxelChunk voxelChunk = new(chunk.coordinate, chunk.voxels);
            AddChunk(voxelChunk);
            chunkUpdated?.Invoke(chunk.coordinate, chunk.initialMesh);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DeleteChunk(Vector3Int coord)
    {
        if (chunks.TryGetValue(coord, out VoxelChunk chunk))
            DeleteChunk(chunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DeleteChunk(VoxelChunk chunk)
    {
        if (chunks.Remove(chunk.coordinate))
        {
            chunk.chunkModified -= ChunkModified;
            chunkDeleted?.Invoke(chunk.coordinate);
            chunk.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddChunk(VoxelChunk chunk)
    {
        chunk.chunkModified += ChunkModified;
        lock (chunks)
            chunks.TryAdd(chunk.coordinate, chunk);
    }
    
    private void ChunkModified(VoxelChunk chunk)
    {
        chunkUpdated?.Invoke(chunk.coordinate, null);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        if (chunks.Count > 0)
        {
            VoxelChunk[] valuesCopy = new VoxelChunk[chunks.Count];
            chunks.Values.CopyTo(valuesCopy, 0);

            foreach (VoxelChunk chunk in valuesCopy)
                DeleteChunk(chunk);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetChunkRelativeCoordinates(Vector3Int worldCoord, out Vector3Int chunkCoord, out Vector3Int voxelCoord)
    {
        chunkCoord = Vector3Int.FloorToInt((Vector3)worldCoord / VoxelChunk.ChunkSize);
        voxelCoord = worldCoord - (chunkCoord * VoxelChunk.ChunkSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel GetVoxel(Vector3Int coord)
    {
        GetChunkRelativeCoordinates(coord, out Vector3Int chunkCoord, out Vector3Int voxelCoord);

        VoxelChunk chunk = GetChunk(chunkCoord);
        if (chunk == null)
            return new Voxel();

        return chunk.GetVoxel(voxelCoord);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(Vector3Int coord, Voxel voxel)
    {
        GetChunkRelativeCoordinates(coord, out Vector3Int chunkCoord, out Vector3Int voxelCoord);

        VoxelChunk chunk = GetChunkOrCreate(chunkCoord);
        chunk.SetVoxel(voxelCoord, voxel);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Voxel GetVoxel(int x, int y, int z)
    {
        return GetVoxel(new Vector3Int(x, y, z));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVoxel(int x, int y, int z, Voxel voxel)
    {
        SetVoxel(new Vector3Int(x, y, z), voxel);
    }
}
