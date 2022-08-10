using UnityEngine;

[PreferBinarySerialization]
public class SerializableChunk : ScriptableObject
{
    public Vector3Int coordinate;
    public Voxel[] voxels;
}
