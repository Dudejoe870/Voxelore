using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[PreferBinarySerialization]
[CreateAssetMenu(menuName = "VoxelSpy/VoxelWorldObject")]
public class VoxelWorldObject : ScriptableObject
{
#if UNITY_EDITOR
    [NonSerialized]
    private Dictionary<Vector3Int, int> chunkDict = new();
#endif

    [NonReorderable]
    [SerializeField]
    private List<SerializableChunk> chunks = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<SerializableChunk> GetChunks()
    {
        return chunks;
    }

#if UNITY_EDITOR
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SerializableChunk AddChunk(Vector3Int coordinate, Voxel[] voxels)
    {
        SerializableChunk chunk = CreateInstance<SerializableChunk>();
        chunk.coordinate = coordinate;
        chunk.voxels = voxels;
        chunks.Add(chunk);
        chunkDict.Add(coordinate, chunks.Count - 1);
        return chunk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SerializableChunk AddChunk(Vector3Int coordinate)
    {
        SerializableChunk chunk = CreateInstance<SerializableChunk>();
        chunk.coordinate = coordinate;
        chunk.voxels = new Voxel[VoxelChunk.ChunkVolume];
        chunks.Add(chunk);
        chunkDict.Add(coordinate, chunks.Count - 1);
        return chunk;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReserveChunks(int amount)
    {
        chunks.Capacity = amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        chunks.Clear();
        chunkDict.Clear();
    }

    public void Awake()
    {
        int i = 0;
        foreach (SerializableChunk chunk in chunks)
        {
            chunkDict[chunk.coordinate] = i;
            ++i;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SerializableChunk SetVoxel(Vector3Int coord, Voxel voxel)
    {
        VoxelWorld.GetChunkRelativeCoordinates(coord, out Vector3Int chunkCoord, out Vector3Int voxelCoord);

        SerializableChunk addedChunk = null;
        if (!chunkDict.TryGetValue(chunkCoord, out int chunkIndex))
        {
            addedChunk = AddChunk(chunkCoord);
            chunkIndex = chunks.Count - 1;
        }
        chunks[chunkIndex].voxels[VoxelChunk.GetVoxelIndex(voxelCoord)] = voxel;
        return addedChunk;
    }
#endif
}
