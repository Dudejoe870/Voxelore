using System;
using System.Runtime.InteropServices;
using UnityEngine;

[Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Voxel
{
    public Color32 color; // 8-bit per channel RGBA color value.

    public Voxel(Color32 color)
    {
        this.color = color;
    }
}
