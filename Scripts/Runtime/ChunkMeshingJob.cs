using System.Runtime.CompilerServices;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using UnityEngine.Rendering;
using Unity.Jobs;
using System;

public struct ChunkVertex
{
    public static readonly VertexAttributeDescriptor[] descriptors =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0)
    };

    public Vector3 position;
    public Vector3 normal;

    public ChunkVertex(Vector3 position, Vector3 normal)
    {
        this.position = position;
        this.normal = normal;
    }
}

public class RebuildJobHandle : IDisposable
{
    public JobHandle handle;
    public Vector3Int chunkCoordinate;
    
    private NativeArray<bool> mask;

    public NativeList<ChunkVertex> vertices;
    public NativeList<ushort> indices;

    // For the case where we load from a Voxel World Object, we need to be able to Dispose of these arrays.
    private NativeArray<Voxel>? voxels;

    private NativeArray<Voxel>? negXVoxels;
    private NativeArray<Voxel>? negYVoxels;
    private NativeArray<Voxel>? negZVoxels;
    private NativeArray<Voxel>? posXVoxels;
    private NativeArray<Voxel>? posYVoxels;
    private NativeArray<Voxel>? posZVoxels;

    private NativeArray<Voxel> nullArray;

    public bool IsCompleted => handle.IsCompleted;

    public RebuildJobHandle(Vector3Int chunkCoord, VoxelWorld world, NativeArray<Voxel> nullArray, Allocator allocator)
    {
        this.nullArray = nullArray;

        chunkCoordinate = chunkCoord;

        mask = new(VoxelChunk.ChunkSlice, allocator, NativeArrayOptions.UninitializedMemory);

        // Initial capacity is just a random number,
        // but it's based off of the Chunk Size
        // to ensure consistency if the Chunk Size is altered.
        vertices = new(VoxelChunk.ChunkVolume / 6, allocator);
        indices = new(VoxelChunk.ChunkVolume / 6, allocator);

        VoxelChunk negX = world.GetChunk(chunkCoord + new Vector3Int(-1, 0, 0));
        VoxelChunk posX = world.GetChunk(chunkCoord + new Vector3Int(1, 0, 0));
        VoxelChunk negY = world.GetChunk(chunkCoord + new Vector3Int(0, -1, 0));
        VoxelChunk posY = world.GetChunk(chunkCoord + new Vector3Int(0, 1, 0));
        VoxelChunk negZ = world.GetChunk(chunkCoord + new Vector3Int(0, 0, -1));
        VoxelChunk posZ = world.GetChunk(chunkCoord + new Vector3Int(0, 0, 1));

        // Keep the fields for these null as we don't want to Dispose of them later in this case.
        NativeArray<Voxel> negXVoxels = negX?.GetNativeArray() ?? nullArray;
        NativeArray<Voxel> posXVoxels = posX?.GetNativeArray() ?? nullArray;
        NativeArray<Voxel> negYVoxels = negY?.GetNativeArray() ?? nullArray;
        NativeArray<Voxel> posYVoxels = posY?.GetNativeArray() ?? nullArray;
        NativeArray<Voxel> negZVoxels = negZ?.GetNativeArray() ?? nullArray;
        NativeArray<Voxel> posZVoxels = posZ?.GetNativeArray() ?? nullArray;

        ChunkMeshingJob job = new(
            world.GetChunk(chunkCoord).GetNativeArray(),
            mask, vertices, indices, 1,
            negXVoxels, posXVoxels, negYVoxels, posYVoxels, negZVoxels, posZVoxels);
        handle = job.Schedule();
    }

#if UNITY_EDITOR
    public RebuildJobHandle(Vector3Int chunkCoord, VoxelWorldObject world, NativeArray<Voxel> nullArray, Allocator allocator)
    {
        this.nullArray = nullArray;

        chunkCoordinate = chunkCoord;

        mask = new(VoxelChunk.ChunkSlice, allocator, NativeArrayOptions.UninitializedMemory);

        vertices = new(VoxelChunk.ChunkVolume / 6, allocator);
        indices = new(VoxelChunk.ChunkVolume / 6, allocator);

        SerializableChunk negX = world.GetChunk(chunkCoord + new Vector3Int(-1, 0, 0));
        SerializableChunk posX = world.GetChunk(chunkCoord + new Vector3Int(1, 0, 0));
        SerializableChunk negY = world.GetChunk(chunkCoord + new Vector3Int(0, -1, 0));
        SerializableChunk posY = world.GetChunk(chunkCoord + new Vector3Int(0, 1, 0));
        SerializableChunk negZ = world.GetChunk(chunkCoord + new Vector3Int(0, 0, -1));
        SerializableChunk posZ = world.GetChunk(chunkCoord + new Vector3Int(0, 0, 1));
        
        negXVoxels = negX != null ? new NativeArray<Voxel>(negX.voxels, allocator) : nullArray;
        posXVoxels = posX != null ? new NativeArray<Voxel>(posX.voxels, allocator) : nullArray;
        negYVoxels = negY != null ? new NativeArray<Voxel>(negY.voxels, allocator) : nullArray;
        posYVoxels = posY != null ? new NativeArray<Voxel>(posY.voxels, allocator) : nullArray;
        negZVoxels = negZ != null ? new NativeArray<Voxel>(negZ.voxels, allocator) : nullArray;
        posZVoxels = posZ != null ? new NativeArray<Voxel>(posZ.voxels, allocator) : nullArray;
        voxels = new(world.GetChunk(chunkCoord).voxels, allocator);
        
        ChunkMeshingJob job = new(
            voxels.Value,
            mask, vertices, indices, 1,
            negXVoxels.Value, posXVoxels.Value, negYVoxels.Value, posYVoxels.Value, negZVoxels.Value, posZVoxels.Value);
        handle = job.Schedule();
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Complete()
    {
        handle.Complete();
    }
    
    public void SetupMesh(Mesh chunkMesh, bool upload = false)
    {
        chunkMesh.subMeshCount = 1;

        chunkMesh.SetVertexBufferParams(vertices.Length, ChunkVertex.descriptors);
        chunkMesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);

        chunkMesh.SetVertexBufferData<ChunkVertex>(
            vertices, 0, 0, vertices.Length, 0,
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
        chunkMesh.SetIndexBufferData<ushort>(
            indices, 0, 0, indices.Length,
            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

        chunkMesh.SetSubMesh(0, new SubMeshDescriptor()
        {
            topology = MeshTopology.Triangles,
            vertexCount = vertices.Length,
            indexCount = indices.Length
        }, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

        if (upload)
        {
            chunkMesh.RecalculateBounds();
            chunkMesh.UploadMeshData(false);
        }
    }

    public void Dispose()
    {
        vertices.Dispose();
        indices.Dispose();
        
        voxels?.Dispose();

        if (negXVoxels != nullArray) negXVoxels?.Dispose();
        if (posXVoxels != nullArray) posXVoxels?.Dispose();
        if (negYVoxels != nullArray) negYVoxels?.Dispose();
        if (posYVoxels != nullArray) posYVoxels?.Dispose();
        if (negZVoxels != nullArray) negZVoxels?.Dispose();
        if (posZVoxels != nullArray) posZVoxels?.Dispose();

        mask.Dispose();
    }
}

[BurstCompile]
public struct ChunkMeshingJob : IJob
{
    [ReadOnly] public NativeArray<Voxel> voxels;

    [ReadOnly] public NativeArray<Voxel> negX;
    [ReadOnly] public NativeArray<Voxel> posX;
    [ReadOnly] public NativeArray<Voxel> negY;
    [ReadOnly] public NativeArray<Voxel> posY;
    [ReadOnly] public NativeArray<Voxel> negZ;
    [ReadOnly] public NativeArray<Voxel> posZ;

    public NativeArray<bool> mask;

    public NativeList<ChunkVertex> vertices;
    public NativeList<ushort> indices;

    public int LOD;

    public ChunkMeshingJob(NativeArray<Voxel> voxels, NativeArray<bool> mask, NativeList<ChunkVertex> vertices, NativeList<ushort> indices, int LOD, NativeArray<Voxel> negX, NativeArray<Voxel> posX, NativeArray<Voxel> negY, NativeArray<Voxel> posY, NativeArray<Voxel> negZ, NativeArray<Voxel> posZ)
    {
        this.voxels = voxels;
        this.mask = mask;

        this.vertices = vertices;
        this.indices = indices;

        this.LOD = LOD;

        this.negX = negX;
        this.posX = posX;
        this.negY = negY;
        this.posY = posY;
        this.negZ = negZ;
        this.posZ = posZ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool GetVoxel(Vector3Int coord)
    {
        if (coord.x < 0)
        {
            if (negX.Length == 0) return false;
            return negX[(VoxelChunk.ChunkSize - 1) + coord.y * VoxelChunk.ChunkSize + coord.z * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }
        else if (coord.x >= VoxelChunk.ChunkSize)
        {
            if (posX.Length == 0) return false;
            return posX[0 + coord.y * VoxelChunk.ChunkSize + coord.z * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }
        else if (coord.y < 0)
        {
            if (negY.Length == 0) return false;
            return negY[coord.x + (VoxelChunk.ChunkSize - 1) * VoxelChunk.ChunkSize + coord.z * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }
        else if (coord.y >= VoxelChunk.ChunkSize)
        {
            if (posY.Length == 0) return false;
            return posY[coord.x + 0 * VoxelChunk.ChunkSize + coord.z * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }
        else if (coord.z < 0)
        {
            if (negZ.Length == 0) return false;
            return negZ[coord.x + coord.y * VoxelChunk.ChunkSize + (VoxelChunk.ChunkSize - 1) * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }
        else if (coord.z >= VoxelChunk.ChunkSize)
        {
            if (posZ.Length == 0) return false;
            return posZ[coord.x + coord.y * VoxelChunk.ChunkSize + 0 * VoxelChunk.ChunkSlice]
                .color.a > 0;
        }

        return voxels[coord.x + coord.y * VoxelChunk.ChunkSize + coord.z * VoxelChunk.ChunkSlice]
                .color.a > 0;
    }

    /*
     * Based off of https://github.dev/weigert/TinyEngine/blob/master/examples/14_Renderpool/source/chunk.h
     */
    public void Execute()
    {
        int CHLOD = VoxelChunk.ChunkSize / LOD;

        for (int i = 0; i < mask.Length; ++i)
            mask[i] = false;

        for (int d = 0; d < 6; ++d)
        {
            int u = (d / 2 + 0) % 3; // u = 0, 0, 1, 1, 2, 2 // Dimension indices
            int v = (d / 2 + 1) % 3; // v = 1, 1, 2, 2, 0, 0
            int w = (d / 2 + 2) % 3; // w = 2, 2, 0, 0, 1, 1

            Vector3Int x = new();
            Vector3Int q = new();
            Vector3Int y = new();

            // Determines the triangle winding order (Clockwise or Counter-Clockwise), as well as the normals / facing direction.
            int n = 2 * (d % 2) - 1; // n = -1, 1, -1, 1, -1, 1
            q[u] = n;
            y[u] = 1;

            for (x[u] = 0; x[u] < CHLOD; ++x[u])
            {
                for (x[v] = 0; x[v] < CHLOD; ++x[v])
                {
                    for (x[w] = 0; x[w] < CHLOD; ++x[w])
                    {
                        int s = x[w] + x[v] * CHLOD;
                        mask[s] = false;

                        bool current = GetVoxel(LOD * x);

                        if (current == false) continue;

                        bool facing = GetVoxel(LOD * (x + q));

                        if (facing == false)
                            mask[s] = current;
                    }
                }

                int width, height;
                for (x[v] = 0; x[v] < CHLOD; ++x[v])
                {
                    for (x[w] = 0; x[w] < CHLOD; x[w] += width)
                    {
                        width = 1;

                        int s = x[w] + x[v] * CHLOD;
                        bool current = mask[s];

                        if (current == false) continue;

                        while (x[w] + width < CHLOD && mask[s + width] == current)
                            ++width;

                        bool quaddone = false;
                        for (height = 1; x[v] + height < CHLOD; ++height)
                        {
                            for (int k = 0; k < width; ++k)
                            {
                                if (mask[s + k + height * CHLOD] != current)
                                {
                                    quaddone = true;
                                    break;
                                }
                            }

                            if (quaddone) break;
                        }

                        for (int l = x[v]; l < x[v] + height; ++l)
                            for (int k = x[w]; k < x[w] + width; ++k)
                                mask[k + l * CHLOD] = false;

                        Vector3Int du = new();
                        du[v] = height;

                        Vector3Int dv = new();
                        dv[w] = width;

                        // Add the Quad depending on whether or not the face needs to be Clockwise or Counter-Clockwise.
                        if (n < 0)
                        {
                            // Triangle 1
                            indices.Add((ushort)(vertices.Length + 0));
                            indices.Add((ushort)(vertices.Length + 2));
                            indices.Add((ushort)(vertices.Length + 1));

                            // Triangle 2
                            indices.Add((ushort)(vertices.Length + 3));
                            indices.Add((ushort)(vertices.Length + 2));
                            indices.Add((ushort)(vertices.Length + 0));

                            // Vertices
                            vertices.Add(new ChunkVertex((x) * LOD, q));
                            vertices.Add(new ChunkVertex((x + du) * LOD, q));
                            vertices.Add(new ChunkVertex((x + du + dv) * LOD, q));
                            vertices.Add(new ChunkVertex((x + dv) * LOD, q));
                        }
                        else
                        {
                            // Triangle 1
                            indices.Add((ushort)(vertices.Length + 0));
                            indices.Add((ushort)(vertices.Length + 2));
                            indices.Add((ushort)(vertices.Length + 1));

                            // Triangle 2
                            indices.Add((ushort)(vertices.Length + 1));
                            indices.Add((ushort)(vertices.Length + 3));
                            indices.Add((ushort)(vertices.Length + 0));

                            // Vertices
                            vertices.Add(new ChunkVertex((x + y) * LOD, q));
                            vertices.Add(new ChunkVertex((x + du + dv + y) * LOD, q));
                            vertices.Add(new ChunkVertex((x + du + y) * LOD, q));
                            vertices.Add(new ChunkVertex((x + dv + y) * LOD, q));
                        }
                    }
                }
            }
        }
    }
}
