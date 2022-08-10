using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshFilter))]
public class SuperChunkRenderer : MonoBehaviour
{
    public WorldRenderer worldRenderer;
    public Vector3Int superChunkCoordinate;
    public Shader voxelShader;

    private MeshRenderer meshRenderer;
    private MeshFilter filter;

    private Mesh superChunkMesh;

    public const int SuperChunkSize = 8; // 8 x 8 x 8 Chunks
    public const int SuperChunkSlice = SuperChunkSize * SuperChunkSize;
    public const int SuperChunkVolume = SuperChunkSlice * SuperChunkSize * SuperChunkSize;

    private Dictionary<Vector3Int, Mesh> chunkMeshes;
    private Dictionary<Vector3Int, int> chunkIndexes;

    private Material material;

    private Texture3D chunkMapTexture;
    private Texture2DArray chunkDataTexture;
    private int lastFreedDataElement;
    private Queue<int> freedDataElements;

    private bool meshDirty;

    private void Start()
    {
        float scale = 1.0f / VoxelChunk.VoxelsToMeter;
        transform.localScale = new Vector3(scale, scale, scale);

        meshRenderer = GetComponent<MeshRenderer>();
        filter = GetComponent<MeshFilter>();

        superChunkMesh = new Mesh()
        {
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };
        filter.mesh = superChunkMesh;
        superChunkMesh.indexFormat = IndexFormat.UInt32;
        
        material = new Material(voxelShader)
        {
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable
        };
        
        chunkMapTexture = new Texture3D(SuperChunkSize, SuperChunkSize, SuperChunkSize, TextureFormat.R16, false)
        {
            hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
        };
        material.SetTexture("_ChunkMap", chunkMapTexture);

        lastFreedDataElement = 0;
        meshDirty = false;

        material.SetFloat("_SuperChunkSize", SuperChunkSize);
        material.SetFloat("_ChunkSize", VoxelChunk.ChunkSize);

        chunkMeshes = new(SuperChunkVolume);
        chunkIndexes = new(SuperChunkVolume);
        freedDataElements = new();

        meshRenderer.sharedMaterial = material;

        worldRenderer.chunkMeshUpdated += OnChunkMeshUpdated;
        worldRenderer.chunkMeshRemoved += OnChunkMeshRemoved;
        worldRenderer.chunkMeshProcessingDone += OnChunkMeshProcessingDone;
    }

    private void UpdateMesh()
    {
        if (chunkMeshes.Count == 0)
            return;

        if (meshDirty)
        {
            CombineInstance[] combineInstances = new CombineInstance[chunkMeshes.Count];
            
            NativeArray<ushort> chunkMapData = new(SuperChunkVolume, Allocator.Temp,
                NativeArrayOptions.ClearMemory);

            int i = 0;
            foreach (KeyValuePair<Vector3Int, Mesh> chunkMesh in chunkMeshes)
            {
                Vector3Int chunkCoordinate = chunkMesh.Key - (superChunkCoordinate * SuperChunkSize);
                int mapIndex = chunkCoordinate.x + (chunkCoordinate.y * SuperChunkSize) + (chunkCoordinate.z * SuperChunkSlice);
                
                chunkMapData[mapIndex] = (ushort)chunkIndexes[chunkMesh.Key];

                combineInstances[i] = new CombineInstance()
                {
                    mesh = chunkMesh.Value,
                    transform = Matrix4x4.TRS(
                        (chunkMesh.Key - (superChunkCoordinate * SuperChunkSize)) * VoxelChunk.ChunkSize,
                        Quaternion.identity, Vector3.one)
                };
                ++i;
            }
            
            chunkMapTexture.SetPixelData(chunkMapData, 0);
            chunkMapTexture.Apply(false, false);
            
            chunkMapData.Dispose();

            superChunkMesh.CombineMeshes(combineInstances);
            filter.mesh = superChunkMesh;
            
            meshDirty = false;
        }
    }
    
    private void OnChunkMeshUpdated(Vector3Int coord, Mesh mesh)
    {
        Vector3Int superChunkCoord = Vector3Int.FloorToInt((Vector3)coord / SuperChunkSize);
        if (superChunkCoord == superChunkCoordinate)
        {
            chunkMeshes[coord] = mesh;
            if (!chunkIndexes.TryGetValue(coord, out int chunkIndex))
            {
                chunkIndex = lastFreedDataElement;
                lastFreedDataElement = freedDataElements.Count > 0 ? freedDataElements.Dequeue() : chunkMeshes.Count;

                chunkIndexes[coord] = chunkIndex;
            }

            meshDirty = true;

            VoxelChunk chunk = worldRenderer.world.GetChunk(coord);
            if (chunk == null)
            {
                Debug.LogError($"Chunk {coord} not found while updating Chunk Meshes for Super Chunk {superChunkCoordinate}");
                return;
            }

            if (chunkDataTexture == null || chunkDataTexture.depth != chunkMeshes.Count)
            {
                Texture2DArray originalTexture = chunkDataTexture;

                chunkDataTexture = new Texture2DArray(
                    VoxelChunk.ChunkSize, VoxelChunk.ChunkSlice, chunkMeshes.Count, TextureFormat.RGBA32, false, false, true)
                {
                    hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor
                };

                material.SetTexture("_ChunkData", chunkDataTexture);

                if (originalTexture != null)
                {
                    for (int i = 0; i < Mathf.Min(chunkDataTexture.depth, originalTexture.depth); ++i)
                        Graphics.CopyTexture(originalTexture, i, chunkDataTexture, i);

                    if (Application.IsPlaying(this))
                        Destroy(originalTexture);
                    else
                        DestroyImmediate(originalTexture, true);
                }
            }

            NativeArray<Color32> chunkData = new(VoxelChunk.ChunkVolume, Allocator.Temp,
                NativeArrayOptions.UninitializedMemory);
            int voxelOffset = 0;
            foreach (Voxel voxel in chunk.GetNativeArray())
                chunkData[voxelOffset++] = voxel.color;

            chunkDataTexture.SetPixelData(chunkData, 0, chunkIndex);
            chunkDataTexture.Apply(false, false);
            
            chunkData.Dispose();
        }
    }

    private void OnChunkMeshRemoved(Vector3Int coord)
    {
        Vector3Int superChunkCoord = Vector3Int.FloorToInt((Vector3)coord / SuperChunkSize);
        if (superChunkCoord == superChunkCoordinate)
        {
            chunkMeshes.Remove(coord);
            if (chunkIndexes.TryGetValue(coord, out int chunkIndex))
            {
                freedDataElements.Enqueue(chunkIndex);
                lastFreedDataElement = chunkIndex;
                chunkIndexes.Remove(coord);
            }

            meshDirty = true;
        }
        UpdateMesh();
    }

    private void OnChunkMeshProcessingDone()
    {
        UpdateMesh();
    }

    private void OnDestroy()
    {
        if (chunkDataTexture != null)
        {
            if (Application.IsPlaying(this))
                Destroy(chunkDataTexture);
            else
                DestroyImmediate(chunkDataTexture, true);
        }

        if (chunkMapTexture != null)
        {
            if (Application.IsPlaying(this))
                Destroy(chunkMapTexture);
            else
                DestroyImmediate(chunkMapTexture, true);
        }

        if (material != null)
        {
            if (Application.IsPlaying(this))
                Destroy(material);
            else
                DestroyImmediate(material, true);
        }

        if (superChunkMesh != null)
        {
            if (Application.IsPlaying(this))
                Destroy(superChunkMesh);
            else
                DestroyImmediate(superChunkMesh, true);
        }

        worldRenderer.chunkMeshUpdated -= OnChunkMeshUpdated;
        worldRenderer.chunkMeshRemoved -= OnChunkMeshRemoved;
        worldRenderer.chunkMeshProcessingDone -= OnChunkMeshProcessingDone;
    }
}
