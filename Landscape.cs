using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

// This class is set to static because we will ever only need ONE of this class.
public static class Landscape {

    // -------------------
    // ------------------- Constant variables
    // -------------------

    // Max available tris
    public const int MAX_TRIS = 200000;

    // Wanted tris
    public const int WANTED_TRIS = 100000;

    // Map settings for the RAW-file
    public const int MAP_SIZE = 4096;
    public const int PATCHES_PER_SIDE = 64;
    public const int PATCH_SIZE = (MAP_SIZE / PATCHES_PER_SIDE);

    // The tolerated distance to the frame variance, if it exceeds this value, then we will split or merge
    public const int VARIANCE_TOLERANCE = 2;

    // The maximum allowed vertices per mesh (Unity-restriction)
    public const int MAX_MESH_VERTS = 65535; // Because there is a 65535 vertex limit, it is divisible by 3

    // Max amount of vertices, should be 3x the size of gDesiredTris because each triangle requires 3 vertices for a non-dictionary solution
    public const int VERTEX_POOL = MAX_TRIS * 3;

    // Amount of TriTreeNodes available for use
    const int TRINODE_POOL = 400000;

    // -------------------
    // ------------------- Public variables
    // -------------------

    // Counting rendered patches
    public static int patchCount = 0;

    // Set a high starting variance - it will change dynamically to render the wanted amount of tris
    public static float frameVariance = 100;

    // Pre-calculate the patch-cube hypotenuse, for use in determining whether a patch is visible or not
    // We want the 3D and not the 2D hypotenuse, because we need to consider that the patch might be very steep with "cube-like" bounds
    // This is used to check whether the patches are on screen or not
    public static int PATCH_HYPOTENUSE = (int)Mathf.Sqrt((PATCH_SIZE * PATCH_SIZE) * 2);
    public static int PATCH_CUBE_HYPOTENUSE = (int)Mathf.Sqrt((PATCH_HYPOTENUSE * PATCH_HYPOTENUSE) + (PATCH_SIZE * PATCH_SIZE)) / 2;
    public static int PATCH_CUBE_HYPOTENUSE_DIV2 = PATCH_CUBE_HYPOTENUSE / 2;

    // Array of bytes in the RAW heightmap
    public static Byte[,] m_HeightMap;

    // -------------------
    // ------------------- Private variables
    // -------------------

    // Pool of nodes for tessellation 
    static TriTreeNode[] m_TriNodePool = new TriTreeNode[TRINODE_POOL];

    // Array of patches to be rendered 
    static Patch[,] m_Patches = new Patch[PATCHES_PER_SIDE, PATCHES_PER_SIDE];

    // Reference to the terrain mesh (note that this patch must have set to indexformat UINT32 and not UINT16
    // otherwise it will only render 2^16 - 1 = 65535 vertices rather than 2^32 - 1 = 4294967295 vertices
    public static Mesh TerrainMesh = GameObject.FindWithTag("Terrain").GetComponent<MeshFilter>().mesh;

    // Pool of nodes for tessellation 
    static TriTreeNode[] m_TriPool = new TriTreeNode[TRINODE_POOL];
    public static Stack<TriTreeNode> TriTreeNodeStack = new Stack<TriTreeNode>();

    // The list of all terrain vertices
    public static List<Vector3> VertexList = new List<Vector3>();

    // The list of triangles
    public static List<int> TriangleList = new List<int>();

    // The stack with indexes to corresponding triangles in the list above
    public static Stack<int> FreeTriangleIndexes = new Stack<int>();

    // The list of UVs
    public static List<Vector2> UVs = new List<Vector2>();

    //
    // Load the RAW-file into Byte-memory
    //
    public static void LoadHeightMap(string _fileName)
    {
        _fileName = Application.dataPath + _fileName;

        int h = MAP_SIZE;
        int w = MAP_SIZE;

        m_HeightMap = new Byte[h + 1, w + 1];

        Debug.Log("Loading heightmap...");

        // Read all pixels into memory as bytes 0-255
        using (var file = System.IO.File.OpenRead(_fileName))
        using (var reader = new System.IO.BinaryReader(file))
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    m_HeightMap[y, x] = (Byte)((int)reader.ReadByte());
                }
            }
        }

        Debug.Log("Heightmap loaded.");
    }

    //
    // The Height Map is stored in an array of values ex. 1024x1024 bytes long.
    // We need to tell patches what heightmap pixels belong to them
    // Initialize all patches
    //
    public static void Init()
    {
        // Start by setting the mesh indexformat to accommodate more than 65535 vertices
        TerrainMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        // Push indexes to free triangles, these indexes will point to available positions in the TriangleList
        for (int i = 0; i < MAX_TRIS; i++)
            FreeTriangleIndexes.Push(i * 3);

        // Add all available UVs, vertices and triangles
        for (int i = 0; i < VERTEX_POOL; i++)
        {
            UVs.Add(new Vector2());
            VertexList.Add(new Vector3());
            TriangleList.Add(i);
        }

        // Add available TriTreeNodes to the stack
        for (int i = 0; i < TRINODE_POOL; i++)
            TriTreeNodeStack.Push(new TriTreeNode());

        // Initiate the patch array
        m_Patches = new Patch[PATCHES_PER_SIDE, PATCHES_PER_SIDE];

        // Assign terrain patches
        for (int i = 0; i < PATCHES_PER_SIDE; i++)
            for (int j = 0; j < PATCHES_PER_SIDE; j++)
                m_Patches[i, j] = new Patch();

        // Temp variable for ease
        Patch patch;

        // Assign and initiate terrain patches
        for (int i = 0; i < PATCHES_PER_SIDE; i++)
        {
            for (int j = 0; j < PATCHES_PER_SIDE; j++)
            {
                patch = (m_Patches[i, j]);

                // Give the terrain the initial tris
                patch.LeftParentTri = AllocateTri();
                patch.RightParentTri = AllocateTri();

                patch.Init(j * PATCH_SIZE, i * PATCH_SIZE, j * PATCH_SIZE, i * PATCH_SIZE, m_HeightMap);
                patch.ComputeVariance();
            }
        }

        // Reset, check for visibility, compute variance and link all patches
        for (int i = 0; i < PATCHES_PER_SIDE; i++)
            for (int j = 0; j < PATCHES_PER_SIDE; j++)
            {
                patch = m_Patches[i, j];

                // Reset the patch
                patch.Reset();

                // Update patch visibility
                patchCount += patch.SetVisibility() ? 1 : 0;

                // Compute variances
                patch.ComputeVariance();

                if (patch.IsVisible)
                {
                    // Link all the patches together.
                    if (j > 0)
                        patch.LeftParentTri.LeftNeighbor = m_Patches[i, j - 1].RightParentTri;

                    if (j < (PATCHES_PER_SIDE - 1))
                        patch.RightParentTri.LeftNeighbor = m_Patches[i, j + 1].LeftParentTri;

                    if (i > 0)
                        patch.LeftParentTri.RightNeighbor = m_Patches[i - 1, j].RightParentTri;

                    if (i < (PATCHES_PER_SIDE - 1))
                        patch.RightParentTri.RightNeighbor = m_Patches[i + 1, j].LeftParentTri;
                }
            }

        Debug.Log("Finished initiating " + PATCHES_PER_SIDE * PATCHES_PER_SIDE + " patches.");
    }

    //
    // Reset all patches
    //
    public static void Reset()
    {
        // Reset counters
        patchCount = 0;

        // Reset and ceck patch visibility
        for (int Y = 0; Y < PATCHES_PER_SIDE; Y++)
            for (int X = 0; X < PATCHES_PER_SIDE; X++)
            {
                m_Patches[Y, X].Reset();

                // Update patch visibility
                patchCount += m_Patches[Y, X].SetVisibility() ? 1 : 0;
            }
    }

    //
    // Split patches and create the theoretical terrain
    //
    public static void Tessellate()
    {
        // If a patch is visible, tessellate it
        for (int i = 0; i < PATCHES_PER_SIDE; i++)
            for (int j = 0; j < PATCHES_PER_SIDE; j++)
                if (m_Patches[i, j].IsVisible)
                    m_Patches[i, j].Tessellate();
    }
   

    //
    // Render the triangles
    //
    public static void Render()
    {
        // If a patch is visible, render it
        for (int i = 0; i < PATCHES_PER_SIDE; i++)
            for (int j = 0; j < PATCHES_PER_SIDE; j++)
                if (m_Patches[i, j].IsVisible)
                    m_Patches[i, j].Render();

        // Assign terrain data
        TerrainMesh.SetVertices(VertexList);
        TerrainMesh.SetTriangles(TriangleList, 0);
        //TerrainMesh.SetUVs(0, UVs);
        //TerrainMesh.RecalculateNormals();
        //TerrainMesh.RecalculateBounds();   

        // Compare how many triangles we rendered with how many we want
        // Adjut frameVariance to a value that will get us to render the desired amount of tris
        //frameVariance = (float)(0.0005F * (MAX_TRIS - FreeTriangleIndexes.Count));
        frameVariance += ((float)(WANTED_TRIS - (FreeTriangleIndexes.Count - 100000)) - (float)WANTED_TRIS) / (float)WANTED_TRIS;

        if (frameVariance < 0)
            frameVariance = 0;
    }

    //
    // Allocate a new node for the mesh 
    //
    public static TriTreeNode AllocateTri()
    {
        // Return if we are out of tris
        if (TriTreeNodeStack.Count <= 0)
            return null;

        return TriTreeNodeStack.Pop();
    }
}
