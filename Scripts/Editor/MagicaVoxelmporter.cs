using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor.AssetImporters;
using UnityEngine;

[ScriptedImporter(1, "vox")]
public class MagicaVoxelmporter : ScriptedImporter
{
    private struct ChunkHeader
    {
        public string id;
        public int numBytesContent;
        public int numBytesChildren;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChunkHeader(BinaryReader reader)
        {
            id = new(reader.ReadChars(4));
            numBytesContent = reader.ReadInt32();
            numBytesChildren = reader.ReadInt32();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ReadStringType(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return new(reader.ReadChars(length));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Dictionary<string, string> ReadDictType(BinaryReader reader)
    {
        Dictionary<string, string> dict;
        
        int length = reader.ReadInt32();
        dict = new(length);
        for (int i = 0; i < length; i++)
        {
            dict.Add(
                ReadStringType(reader),
                ReadStringType(reader));
        }
        return dict;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Matrix4x4 ReadRotationType(byte rotation)
    {
        int firstIndex = (rotation & 0b0011);
        int secondIndex = (rotation & 0b1100) >> 2;
        int thirdIndex = 3 - secondIndex - firstIndex;

        int firstSign = ((rotation & 0b0010000) >> 4) == 0 ? 1 : -1;
        int secondSign = ((rotation & 0b0100000) >> 5) == 0 ? 1 : -1;
        int thirdSign = ((rotation & 0b1000000) >> 6) == 0 ? 1 : -1;

        Vector4 firstRow = new(); firstRow[firstIndex] = firstSign;
        Vector4 secondRow = new(); secondRow[secondIndex] = secondSign;
        Vector4 thirdRow = new(); thirdRow[thirdIndex] = thirdSign;
        Vector4 fourthRow = new(0.0f, 0.0f, 0.0f, 1.0f);

        Matrix4x4 matrix = Matrix4x4.zero;
        matrix.SetRow(0, firstRow);
        matrix.SetRow(1, secondRow);
        matrix.SetRow(2, thirdRow);
        matrix.SetRow(3, fourthRow);

        return matrix;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3Int ParseVector3Int(string str)
    {
        string[] strs = str.Split(' ');
        return new(int.Parse(strs[0]), int.Parse(strs[1]), int.Parse(strs[2]));
    }
    
    private class SceneGraphNode
    {
        public string name;
        public bool hidden;

        public List<SceneGraphNode> children = new();
    }

    private class TransformNode : SceneGraphNode
    {
        public int layer;

        public struct FrameAttributes
        {
            public Matrix4x4 transform;
            public int frameIndex;

            public FrameAttributes(Matrix4x4 transform, int frameIndex)
            {
                this.transform = transform;
                this.frameIndex = frameIndex;
            }
        }

        public List<FrameAttributes> frames = new();
    }

    private class GroupNode : SceneGraphNode
    {
    }

    private class ShapeNode : SceneGraphNode
    {
        public struct ModelInfo
        {
            public int modelIndex;
            public int frameIndex;

            public ModelInfo(int modelIndex, int frameIndex)
            {
                this.modelIndex = modelIndex;
                this.frameIndex = frameIndex;
            }
        }

        public List<ModelInfo> models = new();
    }

    private class SceneGraphReader
    {
        public int rootID = -1;

        private class FileSceneGraphNode
        {
            public Dictionary<string, string> attributes = new();
            public List<int> children = new();
        }

        private class FileGroupNode : FileSceneGraphNode
        {
        }

        private class FileTransformNode : FileSceneGraphNode
        {
            public int layer;
            public List<TransformNode.FrameAttributes> frames = new();
        }

        private class FileShapeNode : FileSceneGraphNode
        {
            public List<ShapeNode.ModelInfo> models = new();
        }

        private Dictionary<int, FileSceneGraphNode> nodes = new();
        
        public bool ReadNode(BinaryReader reader, string type)
        {
            int nodeID = reader.ReadInt32();
            Dictionary<string, string> attributes = ReadDictType(reader);

            switch (type)
            {
                case "nTRN":
                    {
                        FileTransformNode node = new();
                        node.attributes = attributes;

                        node.children.Add(reader.ReadInt32());
                        if (reader.ReadInt32() != -1)
                            Debug.LogWarning("Unexpected value in nTRN");
                        node.layer = reader.ReadInt32();

                        int numFrames = reader.ReadInt32();
                        node.frames.Capacity = numFrames;
                        for (int i = 0; i < numFrames; ++i)
                        {
                            Dictionary<string, string> frameAttributes = ReadDictType(reader);

                            Matrix4x4 translation = Matrix4x4.identity;
                            if (frameAttributes.TryGetValue("_t", out string translationStr))
                                translation = Matrix4x4.Translate(ParseVector3Int(translationStr));

                            Matrix4x4 rotation = Matrix4x4.identity;
                            if (frameAttributes.TryGetValue("_r", out string rotationStr))
                                rotation = ReadRotationType(byte.Parse(rotationStr));
                            
                            int frameIndex = 0;
                            if (frameAttributes.TryGetValue("_f", out string frameIndexStr))
                                frameIndex = int.Parse(frameIndexStr);

                            TransformNode.FrameAttributes frame = new()
                            {
                                transform = translation * rotation,
                                frameIndex = frameIndex
                            };
                            node.frames.Add(frame);
                        }

                        nodes.Add(nodeID, node);
                    }
                    break;
                case "nGRP":
                    {
                        FileGroupNode node = new();
                        node.attributes = attributes;
                        
                        int numChildren = reader.ReadInt32();
                        node.children.Capacity = numChildren;
                        for (int i = 0; i < numChildren; ++i)
                            node.children.Add(reader.ReadInt32());

                        nodes.Add(nodeID, node);
                    }
                    break;
                case "nSHP":
                    {
                        FileShapeNode node = new();
                        node.attributes = attributes;
                        
                        int numModels = reader.ReadInt32();
                        node.models.Capacity = numModels;
                        for (int i = 0; i < numModels; ++i)
                        {
                            int modelIndex = reader.ReadInt32();
                            Dictionary<string, string> modelAttributes = ReadDictType(reader);

                            int frameIndex = 0;
                            if (modelAttributes.TryGetValue("_f", out string frameIndexStr))
                                frameIndex = int.Parse(frameIndexStr);

                            node.models.Add(new ShapeNode.ModelInfo(
                                modelIndex, frameIndex));
                        }

                        nodes.Add(nodeID, node);
                    }
                    break;
                default:
                    return false;
            }

            if (rootID == -1) rootID = nodeID;

            return true;
        }

        private SceneGraphNode ProcessNode(int nodeID)
        {
            SceneGraphNode genericNode = null;

            FileSceneGraphNode node = nodes[nodeID];

            FileTransformNode fTransformNode = node as FileTransformNode;
            FileShapeNode fShapeNode = node as FileShapeNode;

            TransformNode transformNode = null;
            ShapeNode shapeNode = null;

            if (fTransformNode != null)
                genericNode = transformNode = new TransformNode();
            else if (fShapeNode != null)
                genericNode = shapeNode = new ShapeNode();
            else if (node is FileGroupNode)
                genericNode = new GroupNode();

            foreach (int childID in node.children)
                genericNode.children.Add(ProcessNode(childID));

            if (node.attributes.TryGetValue("_name", out string name))
                genericNode.name = name;
            else genericNode.name = "UNAMED";
            
            if (node.attributes.TryGetValue("_hidden", out string hiddenStr))
                genericNode.hidden = hiddenStr == "1";
            else genericNode.hidden = false;

            if (transformNode != null)
            {
                transformNode.layer = fTransformNode.layer;
                transformNode.frames = fTransformNode.frames;
            }
            else if (shapeNode != null)
            {
                shapeNode.models = fShapeNode.models;
            }

            return genericNode;
        }

        public SceneGraphNode CreateSceneGraph()
        {
            if (rootID == -1) return null;

            return ProcessNode(rootID);
        }
    }

    private struct FileVoxel
    {
        public byte x, y, z; // Note: This is different from our XYZ coordinates as Z is up here, and for us Y is up.
        public byte colorIndex;

        public FileVoxel(byte x, byte y, byte z, byte colorIndex)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.colorIndex = colorIndex;
        }
    }
    
    private struct FileModel
    {
        public Vector3Int size;
        public List<FileVoxel> voxels;
    }

    private class ImportingContext
    {
        public SceneGraphReader sceneGraphReader = new();
        public List<FileModel> models = new();
        public Color32[] palette = new Color32[256];
    }

    private void ProcessGraphNode(SceneGraphNode node, AssetImportContext assetCtx, ImportingContext ctx, VoxelWorldObject vwo, Matrix4x4 transform, bool root)
    {
        if (!root)
        {
            if (node is TransformNode)
            {
                TransformNode transformNode = node as TransformNode;
                transform = transformNode.frames[0].transform * transform;
            }
            else if (node is ShapeNode)
            {
                ShapeNode shapeNode = node as ShapeNode;
                foreach (ShapeNode.ModelInfo modelInfo in shapeNode.models)
                {
                    FileModel model = ctx.models[modelInfo.modelIndex];
                    Vector3 modelOffset = Vector3Int.FloorToInt(((Vector3)model.size) * 0.5f) - (Vector3.one * 0.5f);
                    foreach (FileVoxel voxel in model.voxels)
                    {
                        Vector3 objectSpaceCoord = new Vector3(voxel.x, voxel.y, voxel.z) - modelOffset;
                        Vector3Int coord = Vector3Int.FloorToInt(
                            transform.MultiplyPoint(objectSpaceCoord));

                        int colorIndex = voxel.colorIndex;
                        Color32 color = ctx.palette[colorIndex];
                        SerializableChunk addedChunk = vwo.SetVoxel(new Vector3Int(coord.x, coord.z, coord.y), new Voxel(color));
                        if (addedChunk != null)
                        {
                            addedChunk.hideFlags = HideFlags.NotEditable;
                            addedChunk.name = $"Chunk {addedChunk.coordinate}";
                            assetCtx.AddObjectToAsset(
                                $"chunk_{addedChunk.coordinate.x}_{addedChunk.coordinate.y}_{addedChunk.coordinate.z}", addedChunk);
                        }
                    }
                }
            }
        }

        foreach (SceneGraphNode child in node.children)
            ProcessGraphNode(child, assetCtx, ctx, vwo, transform, false);
    }

    public override void OnImportAsset(AssetImportContext ctx)
    {
        VoxelWorldObject vwo = ScriptableObject.CreateInstance<VoxelWorldObject>();
        vwo.hideFlags = HideFlags.NotEditable;

        ImportingContext importCtx = new();

        using (FileStream stream = File.Open(ctx.assetPath, FileMode.Open))
        {
            using (BinaryReader reader = new(stream, Encoding.ASCII, false))
            {
                string magic = new(reader.ReadChars(4));
                if (magic != "VOX ")
                {
                    Debug.LogError("Invalid magic");
                    return;
                }

                int version = reader.ReadInt32();
                if (version != 150 && version != 200)
                {
                    Debug.LogError($"Invalid version number {version}");
                    return;
                }

                ChunkHeader mainHeader = new(reader);
                if (mainHeader.id != "MAIN")
                {
                    Debug.LogError("Invalid main header");
                    return;
                }

                Vector3Int modelSize = Vector3Int.zero;
                int currentModel = -1;

                long fileLength = reader.BaseStream.Length;
                while (reader.BaseStream.Position < fileLength)
                {
                    ChunkHeader header = new(reader);
                    long endOfHeader = reader.BaseStream.Position;
                    int extraSize = 0;
                    switch (header.id)
                    {
                        case "SIZE":
                            if (importCtx.models.Count > 0 && importCtx.models[currentModel].voxels.Count == 0)
                                Debug.LogWarning("No voxels found in previous model");
                            
                            modelSize = new Vector3Int(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                            importCtx.models.Add(new FileModel() 
                            { 
                                size = modelSize,
                                voxels = new List<FileVoxel>()
                            });
                            currentModel = importCtx.models.Count - 1;
                            break;
                        case "XYZI":
                            {
                                if (currentModel == -1)
                                {
                                    Debug.LogError("Found XYZI chunk before SIZE chunk");
                                    return;
                                }
                                int numVoxels = reader.ReadInt32();
                                importCtx.models[currentModel].voxels.Capacity = numVoxels;
                                for (int i = 0; i < numVoxels; ++i)
                                {
                                    importCtx.models[currentModel].voxels.Add(new FileVoxel(
                                        reader.ReadByte(),
                                        reader.ReadByte(),
                                        reader.ReadByte(),
                                        reader.ReadByte()));
                                }
                            }
                            break;
                        case "RGBA":
                            for (int i = 0; i <= 254; ++i)
                            {
                                uint abgr = reader.ReadUInt32();
                                importCtx.palette[i + 1] = new Color32(
                                    (byte)(abgr & 0xFF),
                                    (byte)((abgr >> 8) & 0xFF),
                                    (byte)((abgr >> 16) & 0xFF),
                                    (byte)((abgr >> 24) & 0xFF));
                            }
                            break;
                        case "nTRN":
                        case "nGRP":
                        case "nSHP":
                            importCtx.sceneGraphReader.ReadNode(reader, header.id);
                            break;
                        default:
                            Debug.LogWarning($"Unknown Chunk ID \"{header.id}\" when importing {ctx.assetPath}");
                            break;
                        case "PACK": // Ignored Chunk IDs
                        case "MATL":
                        case "LAYR":
                        case "rOBJ":
                        case "rCAM":
                        case "NOTE":
                        case "IMAP":
                            extraSize = header.numBytesChildren;
                            break;
                    }

                    reader.BaseStream.Seek(endOfHeader + header.numBytesContent + extraSize, SeekOrigin.Begin);
                }
            }
        }

        if (importCtx.models.Count == 0)
        {
            Debug.LogError("No models found");
            return;
        }

        SceneGraphNode sceneGraphRoot = importCtx.sceneGraphReader.CreateSceneGraph();
        if (sceneGraphRoot == null)
        {
            Debug.LogError("No scene graph found");
            return;
        }

        ProcessGraphNode(sceneGraphRoot, ctx, importCtx, vwo, Matrix4x4.identity, true);

        List<(RebuildJobHandle, SerializableChunk)> rebuildJobs = new();
        NativeArray<Voxel> nullArray = new(0, Allocator.TempJob, 
            NativeArrayOptions.UninitializedMemory);
        foreach (SerializableChunk chunk in vwo.GetModifiableChunks())
            rebuildJobs.Add((new RebuildJobHandle(chunk.coordinate, vwo, nullArray, Allocator.TempJob), chunk));
        JobHandle.ScheduleBatchedJobs();

        foreach ((RebuildJobHandle, SerializableChunk) rebuildJob in rebuildJobs)
        {
            rebuildJob.Item1.Complete();
            rebuildJob.Item1.SetupMesh(rebuildJob.Item2.initialMesh, true);
            ctx.AddObjectToAsset($"mesh_{rebuildJob.Item2.coordinate.x}_{rebuildJob.Item2.coordinate.y}_{rebuildJob.Item2.coordinate.z}", 
                rebuildJob.Item2.initialMesh);
            rebuildJob.Item1.Dispose();
        }
        nullArray.Dispose();

        ctx.AddObjectToAsset("level", vwo);
        ctx.SetMainObject(vwo);
    }
}
