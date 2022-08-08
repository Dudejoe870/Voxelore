using System.Collections.Generic;
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

    private Material material;

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

        chunkMeshes = new(SuperChunkVolume);
        
        for (int x = 0; x < SuperChunkSize; ++x)
        {
            for (int y = 0; y < SuperChunkSize; ++y)
            {
                for (int z = 0; z < SuperChunkSize; ++z)
                {
                    Vector3Int chunkCoordinate = new Vector3Int(x, y, z) 
                        + (superChunkCoordinate * SuperChunkSize);
                    if (worldRenderer.chunkMeshes.TryGetValue(chunkCoordinate, out Mesh chunkMesh))
                        chunkMeshes[chunkCoordinate] = chunkMesh;
                }
            }
        }
        UpdateMesh();

        meshRenderer.sharedMaterial = material;

        worldRenderer.chunkMeshUpdated += OnChunkMeshUpdated;
        worldRenderer.chunkMeshRemoved += OnChunkMeshRemoved;
        worldRenderer.chunkMeshProcessingDone += OnChunkMeshProcessingDone;
    }

    private void UpdateMesh()
    {
        if (chunkMeshes.Count == 0)
            return;

        CombineInstance[] combineInstances = new CombineInstance[chunkMeshes.Count];

        int i = 0;
        foreach (KeyValuePair<Vector3Int, Mesh> chunkMesh in chunkMeshes)
        {
            combineInstances[i] = new CombineInstance()
            {
                mesh = chunkMesh.Value,
                transform = Matrix4x4.TRS(
                    chunkMesh.Key * VoxelChunk.ChunkSize, 
                    Quaternion.identity, Vector3.one)
            };
            ++i;
        }

        superChunkMesh.CombineMeshes(combineInstances);
        filter.mesh = superChunkMesh;
    }
    
    private void OnChunkMeshUpdated(Vector3Int coord, Mesh mesh)
    {
        Vector3Int superChunkCoord = Vector3Int.FloorToInt((Vector3)coord / SuperChunkSize);
        if (superChunkCoord == superChunkCoordinate)
            chunkMeshes[coord] = mesh;
    }

    private void OnChunkMeshRemoved(Vector3Int coord)
    {
        chunkMeshes.Remove(coord);
        UpdateMesh();
    }

    private void OnChunkMeshProcessingDone()
    {
        UpdateMesh();
    }

    private void OnDestroy()
    {
        worldRenderer.chunkMeshUpdated -= OnChunkMeshUpdated;
        worldRenderer.chunkMeshRemoved -= OnChunkMeshRemoved;
        worldRenderer.chunkMeshProcessingDone -= OnChunkMeshProcessingDone;
    }
}
