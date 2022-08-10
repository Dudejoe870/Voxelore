using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.CompilerServices;

[ExecuteAlways]
[RequireComponent(typeof(VoxelWorld))]
public class WorldRenderer : MonoBehaviour
{
    public Shader chunkShader;

    [NonSerialized]
    public Dictionary<Vector3Int, Mesh> chunkMeshes;

    private Dictionary<Vector3Int, SuperChunkRenderer> superChunkRenderers;

    [NonSerialized]
    public Action<Vector3Int, Mesh> chunkMeshUpdated;

    [NonSerialized]
    public Action<Vector3Int> chunkMeshRemoved;

    [NonSerialized]
    public Action chunkMeshProcessingDone;

    [NonSerialized]
    public VoxelWorld world;

    private HashSet<Vector3Int> rebuildQueue;
    private Dictionary<Vector3Int, RebuildJobHandle> currentRebuildJobs;

    private NativeArray<Voxel> nullArray;

    private class RebuildJobHandle : IDisposable
    {
        public int lifeTime = 0;

        public JobHandle handle;
        public Vector3Int chunkCoordinate;

        public NativeArray<bool> mask;

        public NativeList<ChunkVertex> vertices;
        public NativeList<ushort> indices;

        public bool IsCompleted => handle.IsCompleted;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Complete()
        {
            handle.Complete();
        }

        public void Dispose()
        {
            vertices.Dispose();
            indices.Dispose();

            mask.Dispose();
        }
    }

    private void OnEnable()
    {
        chunkMeshes = new();

        rebuildQueue = new(4096);
        currentRebuildJobs = new(4096);

        superChunkRenderers = new();

        world = GetComponent<VoxelWorld>();

        nullArray = new(0, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        world.chunkUpdated += ChunkUpdated;
        world.chunkDeleted += ChunkDeleted;
        RenderPipelineManager.beginFrameRendering += BeginFrame;
    }

    private void OnDisable()
    {
        if (currentRebuildJobs != null)
        {
            lock (currentRebuildJobs)
            {
                foreach (RebuildJobHandle handle in currentRebuildJobs.Values)
                {
                    handle.Complete();
                    handle.Dispose();
                }

                currentRebuildJobs.Clear();
            }
        }

        if (superChunkRenderers != null)
        {
            lock (superChunkRenderers)
            {
                foreach (SuperChunkRenderer renderer in superChunkRenderers.Values)
                {
                    if (Application.IsPlaying(this))
                        Destroy(renderer.gameObject);
                    else
                        DestroyImmediate(renderer.gameObject);
                }

                superChunkRenderers.Clear();
            }
        }

        if (chunkMeshes != null)
        {
            lock (chunkMeshes)
            {
                foreach (Mesh mesh in chunkMeshes.Values)
                {
                    if (Application.IsPlaying(this))
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh, true);
                }

                chunkMeshes.Clear();
            }
        }

        nullArray.Dispose();

        world.chunkUpdated -= ChunkUpdated;
        world.chunkDeleted -= ChunkDeleted;
        RenderPipelineManager.beginFrameRendering -= BeginFrame;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessCurrentRebuildJobs()
    {
        if (currentRebuildJobs.Count > 0)
        {
            lock (currentRebuildJobs)
            {
                RebuildJobHandle[] valuesCopy = new RebuildJobHandle[currentRebuildJobs.Count];
                currentRebuildJobs.Values.CopyTo(valuesCopy, 0);

                foreach (RebuildJobHandle handle in valuesCopy)
                {
                    // Make sure the frame lifetime is not exceeded as we use the TempJob allocator
                    // which only allows 4 frames to pass before deallocation.
                    ++handle.lifeTime;
                    if (handle.IsCompleted || handle.lifeTime >= 4)
                    {
                        handle.Complete();

                        if (!chunkMeshes.TryGetValue(handle.chunkCoordinate, out Mesh chunkMesh))
                        {
                            chunkMesh = new Mesh()
                            {
                                hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild
                            };

                            chunkMeshes[handle.chunkCoordinate] = chunkMesh;
                        }

                        chunkMesh.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;

                        chunkMesh.subMeshCount = 1;

                        chunkMesh.SetVertexBufferParams(handle.vertices.Length, ChunkVertex.descriptors);
                        chunkMesh.SetIndexBufferParams(handle.indices.Length, IndexFormat.UInt16);

                        chunkMesh.SetVertexBufferData<ChunkVertex>(
                            handle.vertices, 0, 0, handle.vertices.Length, 0,
                            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
                        chunkMesh.SetIndexBufferData<ushort>(
                            handle.indices, 0, 0, handle.indices.Length,
                            MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

                        chunkMesh.SetSubMesh(0, new SubMeshDescriptor()
                        {
                            topology = MeshTopology.Triangles,
                            vertexCount = handle.vertices.Length,
                            indexCount = handle.indices.Length
                        }, MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

                        // No need to upload the the GPU as these meshes get combined into Super Chunks later on.

                        chunkMeshUpdated?.Invoke(handle.chunkCoordinate, chunkMesh);

                        handle.Dispose();

                        currentRebuildJobs.Remove(handle.chunkCoordinate);
                    }
                }
            }

            chunkMeshProcessingDone?.Invoke();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SchedulePendingChunksForRebuild()
    {
        if (rebuildQueue.Count > 0)
        {
            lock (rebuildQueue)
            {
                int index = 0;
                foreach (Vector3Int chunkCoord in rebuildQueue)
                {
                    if (currentRebuildJobs.TryGetValue(chunkCoord, out RebuildJobHandle currentJob))
                    {
                        currentJob.Complete();
                        currentJob.Dispose();
                        
                        currentRebuildJobs.Remove(chunkCoord);
                    }

                    NativeArray<bool> mask = new(VoxelChunk.ChunkSlice, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    // Initial capacity is just a random number,
                    // but it's based off of the Chunk Size
                    // to ensure consistency if the Chunk Size is altered.
                    NativeList<ChunkVertex> vertices = new(VoxelChunk.ChunkVolume / 6, Allocator.TempJob);
                    NativeList<ushort> indices = new(VoxelChunk.ChunkVolume / 6, Allocator.TempJob);

                    VoxelChunk negX = world.GetChunk(chunkCoord + new Vector3Int(-1,  0,  0));
                    VoxelChunk posX = world.GetChunk(chunkCoord + new Vector3Int( 1,  0,  0));
                    VoxelChunk negY = world.GetChunk(chunkCoord + new Vector3Int( 0, -1,  0));
                    VoxelChunk posY = world.GetChunk(chunkCoord + new Vector3Int( 0,  1,  0));
                    VoxelChunk negZ = world.GetChunk(chunkCoord + new Vector3Int( 0,  0, -1));
                    VoxelChunk posZ = world.GetChunk(chunkCoord + new Vector3Int( 0,  0,  1));

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
                    lock (currentRebuildJobs)
                        currentRebuildJobs.Add(chunkCoord, new RebuildJobHandle()
                        {
                            handle = job.Schedule(),
                            chunkCoordinate = chunkCoord,
                            vertices = vertices,
                            indices = indices,
                            mask = mask
                        });
                    ++index;
                }
                JobHandle.ScheduleBatchedJobs();

                rebuildQueue.Clear();
            }
        }
    }

    private void BeginFrame(ScriptableRenderContext context, Camera[] cameras)
    {
        ProcessCurrentRebuildJobs();
        SchedulePendingChunksForRebuild();
    }

    private void ChunkUpdated(Vector3Int chunkCoord)
    {
        lock (rebuildQueue)
            rebuildQueue.Add(chunkCoord);

        lock (superChunkRenderers)
        {
            Vector3Int superChunkCoord = Vector3Int.FloorToInt((Vector3)chunkCoord / SuperChunkRenderer.SuperChunkSize);
            if (!superChunkRenderers.ContainsKey(superChunkCoord))
            {
                GameObject chunkObject = new($"Super Chunk {superChunkCoord}")
                {
                    hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor | HideFlags.NotEditable
                };
                
                chunkObject.transform.parent = transform;
                chunkObject.transform.localPosition = ((Vector3)superChunkCoord * (SuperChunkRenderer.SuperChunkSize * VoxelChunk.ChunkSize)) 
                    * (1.0f / VoxelChunk.VoxelsToMeter);
                chunkObject.transform.localRotation = Quaternion.identity;
                
                var superChunkRenderer = chunkObject.AddComponent<SuperChunkRenderer>();
                superChunkRenderer.superChunkCoordinate = superChunkCoord;
                superChunkRenderer.worldRenderer = this;
                superChunkRenderer.voxelShader = chunkShader;

                superChunkRenderers[superChunkCoord] = superChunkRenderer;
            }
        }
    }

    private void ChunkDeleted(Vector3Int chunkCoord)
    {
        lock (rebuildQueue)
            rebuildQueue.Remove(chunkCoord);

        chunkMeshRemoved?.Invoke(chunkCoord);
    }
}
