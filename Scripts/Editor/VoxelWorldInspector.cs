using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(VoxelWorld))]
public class VoxelWorldInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        VoxelWorld world = (VoxelWorld)target;
        if (GUILayout.Button("Clear Voxels"))
        {
            world.Clear();
        }

        if (GUILayout.Button("Load From World Asset"))
        {
            world.LoadVoxelWorldAsset(world.worldAsset);
        }
    }
}
