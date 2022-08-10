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
    private Dictionary<Vector3Int, WorldRebuildJobHandle> currentRebuildJobs;

    private NativeArray<Voxel> nullArray;

    private class WorldRebuildJobHandle : RebuildJobHandle
    {
        public int lifeTime = 0;
        
        public WorldRebuildJobHandle(Vector3Int chunkCoord, VoxelWorld world, NativeArray<Voxel> nullArray, Allocator allocator) 
            : base(chunkCoord, world, nullArray, allocator)
        {
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
                WorldRebuildJobHandle[] valuesCopy = new WorldRebuildJobHandle[currentRebuildJobs.Count];
                currentRebuildJobs.Values.CopyTo(valuesCopy, 0);

                foreach (WorldRebuildJobHandle handle in valuesCopy)
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

                        handle.SetupMesh(chunkMesh);

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
                foreach (Vector3Int chunkCoord in rebuildQueue)
                {
                    if (currentRebuildJobs.TryGetValue(chunkCoord, out WorldRebuildJobHandle currentJob))
                    {
                        currentJob.Complete();
                        currentJob.Dispose();
                        
                        currentRebuildJobs.Remove(chunkCoord);
                    }
                    
                    lock (currentRebuildJobs)
                        currentRebuildJobs.Add(chunkCoord, 
                            new WorldRebuildJobHandle(chunkCoord, world, nullArray, Allocator.TempJob));
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

    private void ChunkUpdated(Vector3Int chunkCoord, Mesh initialMesh)
    {
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

        if (initialMesh != null)
        {
            chunkMeshes[chunkCoord] = initialMesh;
            chunkMeshUpdated?.Invoke(chunkCoord, initialMesh);
        }
        else
        {
            lock (rebuildQueue)
                rebuildQueue.Add(chunkCoord);
        }
    }

    private void ChunkDeleted(Vector3Int chunkCoord)
    {
        lock (rebuildQueue)
            rebuildQueue.Remove(chunkCoord);

        chunkMeshRemoved?.Invoke(chunkCoord);
    }
}
