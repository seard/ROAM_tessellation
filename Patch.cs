using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

//
// Patch Class
// Store information needed at the Patch level
//
public class Patch
{
    // -------------------
    // ------------------- Constant variables
    // -------------------

    // Tree depth, 9 will be sufficient as long as MAP_SIZE / PATCHES_PER_SIDE = 64
    private const int VARIANCE_DEPTH = 9;

    // -------------------
    // ------------------- Public variables
    // -------------------

    // Bool for whether the patch is visible or not
    public bool IsVisible;

    // The ancestor tris which are to be split into new tris
    public TriTreeNode LeftParentTri;                                   // Left base triangle tree node
    public TriTreeNode RightParentTri;                                   // Right base triangle tree node

    // Each patch receives its own mesh
    public Mesh m_Mesh;

    // -------------------
    // ------------------- Private variables
    // -------------------

    // Reference to the heightmap in Landscape
    Byte[,] m_HeightMap;

    // World coordinates of this patch
    int m_WorldX, m_WorldY;
    // Coordinates [0,0] of patch in the height map
    int m_HeightX, m_HeightY;                                   
    // Center positions XY of patch
    int patchCenterX, patchCenterY;

    // The bitshift operator shifts 1 in binary X steps to the left.
    // Byte is an int8 (8 bit integer)
    // Set depth of the sides of the tree
    Byte[] m_VarianceLeft = new Byte[1 << (VARIANCE_DEPTH)];
    Byte[] m_VarianceRight = new Byte[1 << (VARIANCE_DEPTH)];

    // Temporary variance used in getting the new variance of the left & right sides of the tree
    Byte[] m_TempVariance = new Byte[1 << (VARIANCE_DEPTH)];

    //
    // The recursive split which creates new child tris if possible
    //
    public void Split(TriTreeNode tri)
    {
        // Return if we are already split
        if (tri.LeftChild != null)
            return;

        // If not in a diamond with our BaseNeighbor, split the neighbor first
        if (tri.BaseNeighbor != null && (tri.BaseNeighbor.BaseNeighbor != tri))
            Split(tri.BaseNeighbor);

        // Attempt to create new children nodes
        tri.LeftChild = Landscape.AllocateTri();
        tri.RightChild = Landscape.AllocateTri();

        // Creation of two children succeeded
        // If current triangle is rendered, stop rendering it and free the triangles
        if (tri.isRendered)
        {
            Landscape.FreeTriangleIndexes.Push(tri.indexTri);

            Landscape.VertexList[tri.indexTri] = new Vector3();
            Landscape.VertexList[tri.indexTri + 1] = new Vector3();
            Landscape.VertexList[tri.indexTri + 2] = new Vector3();

            tri.indexTri = -1;

            // We will not begin to split - recursively reset render flags
            ResetRenderflags(tri);
        }

        // We will not begin to split - recursively reset tessellation flags
        ResetTessellateflags(tri);


        // Note:
        // When we handle tris, we pivot from the apex vertex, not the left or right vertices. (Which is when we look at our BaseNeighbor)
        // Ex.
        // leftParentTri.LeftNeighbor will be above the patch, and leftParentTri.RightNeighbor will be to the left of the patch
        //
        // leftParent.LeftNeighbor
        //
        // --------------------
        // |  left          / |
        // | Parent      /    |
        // |          /       |  rightParent.RightNeighbor
        // |       /   right  |
        // |    /     Parent  |
        // | /                |
        // --------------------
        //
        // rightParent.LeftNeighbor


        // Pass useful parent information to children
        tri.LeftChild.BaseNeighbor = tri.LeftNeighbor;
        tri.LeftChild.LeftNeighbor = tri.RightChild;

        tri.LeftChild.Parent = tri;
        tri.RightChild.Parent = tri;

        tri.RightChild.BaseNeighbor = tri.RightNeighbor;
        tri.RightChild.RightNeighbor = tri.LeftChild;

        // Link our Left Neighbor to the new children
        if (tri.LeftNeighbor != null)
        {
            if (tri.LeftNeighbor.BaseNeighbor == tri)
                tri.LeftNeighbor.BaseNeighbor = tri.LeftChild;
            else if (tri.LeftNeighbor.LeftNeighbor == tri)
                tri.LeftNeighbor.LeftNeighbor = tri.LeftChild;
            else if (tri.LeftNeighbor.RightNeighbor == tri)
                tri.LeftNeighbor.RightNeighbor = tri.LeftChild;
        }

        // Link our Right Neighbor to the new children
        if (tri.RightNeighbor != null)
        {
            if (tri.RightNeighbor.BaseNeighbor == tri)
                tri.RightNeighbor.BaseNeighbor = tri.RightChild;
            else if (tri.RightNeighbor.RightNeighbor == tri)
                tri.RightNeighbor.RightNeighbor = tri.RightChild;
            else if (tri.RightNeighbor.LeftNeighbor == tri)
                tri.RightNeighbor.LeftNeighbor = tri.RightChild;
        }

        // Link our Base Neighbor to the new children
        if (tri.BaseNeighbor != null)
        {
            if (tri.BaseNeighbor.LeftChild != null)
            {
                tri.BaseNeighbor.LeftChild.RightNeighbor = tri.RightChild;
                tri.BaseNeighbor.RightChild.LeftNeighbor = tri.LeftChild;
                tri.LeftChild.RightNeighbor = tri.BaseNeighbor.RightChild;
                tri.RightChild.LeftNeighbor = tri.BaseNeighbor.LeftChild;
            }
            else
                Split(tri.BaseNeighbor);  // Now split BaseNeighbor
        }
        else
        {
            // An edge triangle, trivial case.
            tri.LeftChild.RightNeighbor = null;
            tri.RightChild.LeftNeighbor = null;
        }
    }

    //
    // Split triangles according to variance
    //
    public void RecursTessellate(TriTreeNode tri, int leftX, int leftY, int rightX, int rightY, int apexX, int apexY, int node)
    {
        float TriVariance = 0;

        // Get hypotenuse XY positions
        int centerX = (leftX + rightX) >> 1;
        int centerY = (leftY + rightY) >> 1;

        // If we are not visiting a leaf node
        if (node < (1 << VARIANCE_DEPTH))
        {
            // Note:
            // This solution is 22.5% faster, but it will render 30-50% of triangles behind the camera

            // If the terrain is not flat here, calculate TriVariance
            if (m_TempVariance[node] > 1)
            {
                // Calculate distance from the camera to the node
                float distance = 1.0f + Vector3.Distance(Camera.main.transform.position, new Vector3(centerX, m_HeightMap[centerX, centerY], centerY));

                // Consider distance and variance
                TriVariance = ((float)m_TempVariance[node] * Landscape.MAP_SIZE * 2.0F) / distance;
            }

            /*
            // This solution is slower, but it will render barely any useless triangles, < 10%
            if (m_TempVariance[node] > 1)
            {
                // Define the points and vectors
                Vector3 C = Camera.main.transform.position;
                Vector3 F = Camera.main.transform.forward;
                Vector3 P = new Vector3(centerX, m_HeightMap[centerX, centerY], centerY);

                // Calculate the distances between node and camera
                float Distance_P_C = Vector3.Distance(P, C);
                float Distance_FPC_P = Vector3.Distance(((Distance_P_C) * F) + C, P);

                // If the distance to the patch is greater than the distance from the perfect position to the patch, it is certainly within view
                // We add PATCH_HYPOTENUSE to account for the width of the patches so that we don't skip rendering a patch
                if (Distance_P_C + Landscape.PATCH_CUBE_HYPOTENUSE_DIV2 > Distance_FPC_P)
                {
                    TriVariance = ((float)m_TempVariance[node] * Landscape.MAP_SIZE * 2.0F) / Distance_P_C;
                }
            }
            */
        }

        // If node >= 512, then we must split because we are a leaf node
        // If we are not fully tessellated and TriVariance exceeds gFrameVaraince (which alternates to optimally spread triangles) then split
        // Adding VARIANCE_TOLERANCE-check increases efficiency by roughly 17%
        if (!tri.isTessellated && ((node >= (1 << VARIANCE_DEPTH)) || (TriVariance > Landscape.frameVariance + Landscape.VARIANCE_TOLERANCE)))
        {
            // Attempt to split this tri
            Split(tri);

            // If this tri was split and we are not too small, recurse and repeat for children
            if (tri.LeftChild != null && ((Mathf.Abs(leftX - rightX) >= 3) || (Mathf.Abs(leftY - rightY) >= 3)))
            {
                RecursTessellate(tri.LeftChild, apexX, apexY, leftX, leftY, centerX, centerY, node << 1);
                RecursTessellate(tri.RightChild, rightX, rightY, apexX, apexY, centerX, centerY, 1 + (node << 1));
            }
        }
        // If we can tolerate a merge, we are not splitting and children are eligible for a merge, merge
        else if (TriVariance < Landscape.frameVariance - Landscape.VARIANCE_TOLERANCE && tri.LeftChild != null && tri.isRendered)
        {
            MergeDown(tri);
        }
        

        // If all children are fully tessellated tessellated, then we are fully tessellated
        if (tri.LeftChild != null && tri.LeftChild.isTessellated && tri.RightChild.isTessellated)
            tri.isTessellated = true;
        // Else if we are a leaf node, then we are fully tessellated
        else if(node >= (1 << VARIANCE_DEPTH))
            tri.isTessellated = true;
    }

    //
    // Reset all ancestor renderflags
    //
    public void ResetRenderflags(TriTreeNode tri)
    {
        if (tri.Parent != null)
            ResetRenderflags(tri.Parent);

        tri.isRendered = false;
    }

    //
    // Reset all ancestor tessellateflags
    //
    public void ResetTessellateflags(TriTreeNode tri)
    {
        if (tri.Parent != null)
            ResetRenderflags(tri.Parent);

        tri.isTessellated = false;
    }


    //
    // Recursively render all triangles
    //
    public void RecursRender(TriTreeNode tri, int leftX, int leftY, int rightX, int rightY, int apexX, int apexY)
    {
        // If we are not rendered all the way down the tree, enter
        if (!tri.isRendered)
        {
            if (tri.LeftChild != null)                 // All non-leaf nodes have both children, so just check for one
            {
                // Get the center of the hypotenuse with a graceful bitshift operation
                int centerX = (leftX + rightX) >> 1;
                int centerY = (leftY + rightY) >> 1;

                // Traverse down the sides of the tree
                RecursRender(tri.LeftChild, apexX, apexY, leftX, leftY, centerX, centerY);
                RecursRender(tri.RightChild, rightX, rightY, apexX, apexY, centerX, centerY);

                // If our left and right children are fully rendered, then so are we
                if (tri.LeftChild.isRendered && tri.RightChild.isRendered)
                    tri.isRendered = true;
            }
            // Else we are a leaf, so render us
            else if (Landscape.FreeTriangleIndexes.Count > 0)
            {
                // Set the height of all three triangle vertices
                float leftZ = m_HeightMap[leftX, leftY];
                float rightZ = m_HeightMap[rightX, rightY];
                float apexZ = m_HeightMap[apexX, apexY];

                // Render with indexed vertices and triangles
                RenderIndexed(tri, leftX, leftY, leftZ, rightX, rightY, rightZ, apexX, apexY, apexZ);
            }
        }
    }

    // Render by simply adding vertices to the VertexList, this will result in an excessive amount of duplicates, it's a middle-ground between OpenGL and Dictionary, it uses indexed triangles and vertices
    void RenderIndexed (TriTreeNode tri, float leftX, float leftY, float leftZ, float rightX, float rightY, float rightZ, float apexX, float apexY, float apexZ)
    {
        // Get the index pointing to a place in VertexList and TriangleList where 3 consecutive vertices are free
        int freeIndex = Landscape.FreeTriangleIndexes.Pop();

        // We are a leaf node and we are rendered
        tri.isRendered = true;
        tri.indexTri = freeIndex;

        Landscape.VertexList[freeIndex] = new Vector3(leftX, leftZ, leftY);
        Landscape.VertexList[freeIndex + 1] = new Vector3(rightX, rightZ, rightY);
        Landscape.VertexList[freeIndex + 2] = new Vector3(apexX, apexZ, apexY);

        //Landscape.UVs[freeIndex] = new Vector2(leftX, leftY);
        //Landscape.UVs[freeIndex + 1] = new Vector2(rightX, rightY);
        //Landscape.UVs[freeIndex + 2] = new Vector2(apexX, apexY);
    }

    //
    // This recursive function will traverse down the heightmap to find the max variance
    // which will then be used as reference to know how far down to split the triangles
    //
    public Byte RecursComputeVariance(  int leftX, int leftY, Byte leftZ,
                                        int rightX, int rightY, Byte rightZ,
                                        int apexX, int apexY, Byte apexZ,
                                        int node)
    {
        // Bitshifting to the right >> 1 is faster way of dividing by 2 and rounding the value
        // We round the value because we work with Byte int8
        // Therefore this will give us the middle of the two added values, which become the center of the hypotenuse
        int centerX = (leftX + rightX) >> 1;
        int centerY = (leftY + rightY) >> 1;
        Byte myVariance;

        // Get the height value at the middle of the Hypotenuse
        Byte centerZ = m_HeightMap[centerX, centerY];

        // The variance of this node is the HeightMap value at this position minus the interpolated height
        myVariance = (Byte)Mathf.Abs((int)centerZ - (((int)leftZ + (int)rightZ) >> 1));

        // So we compare the variance of the current triangle and all of the deeper down triangles
        if ((Mathf.Abs(leftX - rightX) >= 8) ||
             (Mathf.Abs(leftY - rightY) >= 8))
        {
            // Final Variance for this node is the max of it's own variance and that of it's children.
            // We run variance for left and right triangle (we make apex=left, left=right and center=apex)
            // Get into it and you will understand why, because the hypotenuse is between left and right
            // The increment of node counts through the bintree
            myVariance = (Byte)Mathf.Max(myVariance, RecursComputeVariance(apexX, apexY, apexZ, leftX, leftY, leftZ, centerX, centerY, centerZ, node << 1));
            myVariance = (Byte)Mathf.Max(myVariance, RecursComputeVariance(rightX, rightY, rightZ, apexX, apexY, apexZ, centerX, centerY, centerZ, 1 + (node << 1)));
        }

        // Store the final variance for this node.
        // The recursion will do a left -> right root-search in the bintree. Only the leftmost will be 2-4-8-16-32-64
        //					1
        //				2       3
        //			4      5|6		7
        //		8	   9|10...13|14		15
        //	16	 17|18...............30     31
        if (node < (1 << VARIANCE_DEPTH))
            m_TempVariance[node] = (Byte)(1 + myVariance);

        return myVariance;
    }

    //
    // Compute the variances of all theoretical triangles in this patch
    //
    public void ComputeVariance()
    {
        // Compute variance on each of the ancestor triangles (these operations are fairly quick)
        m_TempVariance = m_VarianceLeft;
        RecursComputeVariance(  m_HeightX,                          m_HeightY + Landscape.PATCH_SIZE,   m_HeightMap[m_HeightX, m_HeightY + Landscape.PATCH_SIZE],
                                m_HeightX + Landscape.PATCH_SIZE,   m_HeightY,                          m_HeightMap[m_HeightX + Landscape.PATCH_SIZE, m_HeightY],
                                m_HeightX,                          m_HeightY,                          m_HeightMap[m_HeightX, m_HeightY],
                                1);

        m_TempVariance = m_VarianceRight;
        RecursComputeVariance(  m_HeightX + Landscape.PATCH_SIZE,   m_HeightY,                          m_HeightMap[m_HeightX + Landscape.PATCH_SIZE, m_HeightY],
                                m_HeightX,                          m_HeightY + Landscape.PATCH_SIZE,   m_HeightMap[m_HeightX, m_HeightY + Landscape.PATCH_SIZE],
                                m_HeightX + Landscape.PATCH_SIZE,   m_HeightY + Landscape.PATCH_SIZE,   m_HeightMap[m_HeightX + Landscape.PATCH_SIZE, m_HeightY + Landscape.PATCH_SIZE],
                                1);
    }

    //
    // The static half of the Patch Class
    //
    public void Init(int heightX, int heightY, int worldX, int worldY, Byte[,] hMap)
    {
        // Attach the two m_Base triangles together
        LeftParentTri.BaseNeighbor = RightParentTri;
        RightParentTri.BaseNeighbor = LeftParentTri;

        // Store Patch offsets for the world and heightmap.
        m_WorldX = worldX;
        m_WorldY = worldY;
        m_HeightX = heightX;
        m_HeightY = heightY;

        // Get patch's center point
        patchCenterX = m_WorldX + Landscape.PATCH_SIZE / 2;
        patchCenterY = m_WorldY + Landscape.PATCH_SIZE / 2;

        // Reference to the heightmap in Landscape
        m_HeightMap = hMap;

        // Initialize visibility flag
        IsVisible = false;
    }

    //
    // Reset the patch (kept in case we want to expand the solution)
    //
    public void Reset()
    {
        IsVisible = false;
    }

    //
    // Set our visibility flag
    //
    public bool SetVisibility()
    {
        // Save the current state of visibility
       // bool tempVisibility = IsVisible;

        // World To Viewport Point, this is a standard way of checking if a point is in veiw or not
        //IsVisible = VisibilityWTVP(patchCenterX, patchCenterY);

        // Notes:
        // This solution is roughly 24% faster than WTVP
        // Camera Forward Point, a graceful way of determining if something is in view or not
        // This allows for checking if the bounding box is within view
        //IsVisible = VisibilityCFP(patchCenterX, patchCenterY);
        /*
        // If the patch was visible and has now become invisible, free all triangles in this patch
        if (!IsVisible && tempVisibility != IsVisible)
        {
            Debug.Log("Collapsing patch...");
            // Collapse the left side of the tree
            CollapsePatchTree(LeftParentTri);
            // Collapse the right side of the tree
            CollapsePatchTree(RightParentTri);
        }
        */
        // ------------------- WARNING: Added for debugging
        IsVisible = true;
        return IsVisible;
    }

    //
    // Collapse the tree and free up any now useless triangles for new use
    //
    public void CollapsePatchTree(TriTreeNode tri)
    {
        MergeDown(tri);
    }

    // Returns true if the distance from P -> F*PC is less than PC
    bool VisibilityCFP(int patchCenterX, int patchCenterY)
    {
        // Define the points and vectors
        Vector3 C = Camera.main.transform.position;
        Vector3 F = Camera.main.transform.forward;
        Vector3 P = new Vector3(patchCenterX, m_HeightMap[patchCenterX, patchCenterY], patchCenterY);

        // Calculate the distances between patch and camera
        // Distance uses SQRT, find a better solution
        float Distance_P_C = Vector3.Distance(P, C);
        float Distance_FPC_P = Vector3.Distance(((Distance_P_C) * F) + C, P);

        // If the distance to the patch is greater than the distance from the perfect position to the patch, it is certainly within view
        // We add PATCH_HYPOTENUSE to account for the width of the patches so that we don't skip rendering a patch
        if (Distance_P_C + Landscape.PATCH_CUBE_HYPOTENUSE > Distance_FPC_P)
            return true;

        return false;
    }

    // Returns true if the patch is on screen
    bool VisibilityWTVP(int patchCenterX, int patchCenterY)
    {
        Vector3 screenPos = Camera.main.WorldToViewportPoint(new Vector3(patchCenterX, 0, patchCenterY));
        bool onScreen = screenPos.z > 0 && screenPos.x > 0 && screenPos.x < 1 && screenPos.y > 0 && screenPos.y < 1;

        return onScreen;
    }

    //
    // Create the theoretical terrain
    //
    public void Tessellate()
    {
        // Split the ancestor triangles and possibly also their children
        m_TempVariance = m_VarianceLeft;
        RecursTessellate(LeftParentTri,
                            m_WorldX, m_WorldY + Landscape.PATCH_SIZE,
                            m_WorldX + Landscape.PATCH_SIZE, m_WorldY,
                            m_WorldX, m_WorldY,
                            1);

        m_TempVariance = m_VarianceRight;
        RecursTessellate(RightParentTri,
                            m_WorldX + Landscape.PATCH_SIZE, m_WorldY,
                            m_WorldX, m_WorldY + Landscape.PATCH_SIZE,
                            m_WorldX + Landscape.PATCH_SIZE, m_WorldY + Landscape.PATCH_SIZE,
                            1);
    }

    //
    // Render the mesh
    //
    public void Render()
    {
        // Recursively render leaf nodes
        RecursRender(LeftParentTri,
                    m_HeightX,                          m_HeightY + Landscape.PATCH_SIZE,
                    m_HeightX + Landscape.PATCH_SIZE,   m_HeightY,
                    m_HeightX,                          m_HeightY);

        RecursRender(RightParentTri,
                    m_HeightX + Landscape.PATCH_SIZE,   m_HeightY,
                    m_HeightX,                          m_HeightY + Landscape.PATCH_SIZE,
                    m_HeightX + Landscape.PATCH_SIZE,   m_HeightY + Landscape.PATCH_SIZE);
    }

    //
    // Merge the children
    //
    void Merge(TriTreeNode tri)
    {
        // If there is no child, then we are already merged
        if (tri.LeftChild == null)
            return;

        // Get the base neighbor of the left child and connect to parent
        if (tri.LeftChild.BaseNeighbor != null)
        {
            if (tri.LeftChild.BaseNeighbor.LeftNeighbor == tri.LeftChild)
                tri.LeftChild.BaseNeighbor.LeftNeighbor = tri;
            if (tri.LeftChild.BaseNeighbor.RightNeighbor == tri.LeftChild)
                tri.LeftChild.BaseNeighbor.RightNeighbor = tri;
            if (tri.LeftChild.BaseNeighbor.BaseNeighbor == tri.LeftChild)
            {
                tri.LeftChild.BaseNeighbor.BaseNeighbor = tri;
                if (tri.LeftNeighbor == tri.LeftChild.BaseNeighbor.Parent)
                    tri.LeftNeighbor = tri.LeftChild.BaseNeighbor;
            }

            // For ease
            TriTreeNode baseNeighbor = tri.LeftChild.BaseNeighbor;

            // We need to check if tri.LeftChild.BaseNeighbor has a parent which is also bound to this triangle
            // then we also need to update its connections to be set to the parent
            if (baseNeighbor.Parent != null)
            {
                if (baseNeighbor.Parent.LeftNeighbor == tri.LeftChild)
                    baseNeighbor.Parent.LeftNeighbor = tri;
                if (baseNeighbor.Parent.RightNeighbor == tri.LeftChild)
                    baseNeighbor.Parent.RightNeighbor = tri;
                if (baseNeighbor.Parent.BaseNeighbor == tri.LeftChild)
                    baseNeighbor.Parent.BaseNeighbor = tri;
            }
        }

        // Same for the right child
        if (tri.RightChild.BaseNeighbor != null)
        {
            if (tri.RightChild.BaseNeighbor.LeftNeighbor == tri.RightChild)
                tri.RightChild.BaseNeighbor.LeftNeighbor = tri;
            if (tri.RightChild.BaseNeighbor.RightNeighbor == tri.RightChild)
                tri.RightChild.BaseNeighbor.RightNeighbor = tri;
            if (tri.RightChild.BaseNeighbor.BaseNeighbor == tri.RightChild)
            {
                tri.RightChild.BaseNeighbor.BaseNeighbor = tri;
                if (tri.RightNeighbor == tri.RightChild.BaseNeighbor.Parent)
                    tri.RightNeighbor = tri.RightChild.BaseNeighbor;
            }

            // For getting a shorter expression
            TriTreeNode baseNeighbor = tri.RightChild.BaseNeighbor;

            // We need to check if tri.RightChild.BaseNeighbor has a parent which is also bound to this triangle
            // then we also need to update its connections to be set to the parent
            if (baseNeighbor.Parent != null)
            {
                if (baseNeighbor.Parent.LeftNeighbor == tri.RightChild)
                    baseNeighbor.Parent.LeftNeighbor = tri;
                if (baseNeighbor.Parent.RightNeighbor == tri.RightChild)
                    baseNeighbor.Parent.RightNeighbor = tri;
                if (baseNeighbor.Parent.BaseNeighbor == tri.RightChild)
                    baseNeighbor.Parent.BaseNeighbor = tri;
            }
        }

        // Free the LeftChild index and let it be used by another rendered triangle
        if (tri.LeftChild.isRendered)
        {
            Landscape.VertexList[tri.LeftChild.indexTri] = new Vector3();
            Landscape.VertexList[tri.LeftChild.indexTri + 1] = new Vector3();
            Landscape.VertexList[tri.LeftChild.indexTri + 2] = new Vector3();

            Landscape.FreeTriangleIndexes.Push(tri.LeftChild.indexTri);

            ResetRenderflags(tri.LeftChild);
        }

        // Free the RightChild index and let it be used by another rendered triangle
        if (tri.RightChild.isRendered)
        {
            Landscape.VertexList[tri.RightChild.indexTri] = new Vector3();
            Landscape.VertexList[tri.RightChild.indexTri + 1] = new Vector3();
            Landscape.VertexList[tri.RightChild.indexTri + 2] = new Vector3();

            Landscape.FreeTriangleIndexes.Push(tri.RightChild.indexTri);

            ResetRenderflags(tri.RightChild);
        }

        // Add two new nodes to TriTreeNodeStack, available for allocation
        // The garbage collector will automatically detect and destroy tri.LeftChild and RightChild
        Landscape.TriTreeNodeStack.Push(new TriTreeNode());
        Landscape.TriTreeNodeStack.Push(new TriTreeNode());

        // Make the current node a leaf node
        tri.LeftChild = null;
        tri.RightChild = null;
    }

    //
    // Mergable returns true if we can merge, false if we can't
    //
    bool Mergable(TriTreeNode tri)
    {
        // If we are merged, return false
        if (tri.LeftChild == null)
            return false;

        // If there are no grandchildren, return true
        if ((tri.LeftChild.LeftChild == null) && (tri.RightChild.LeftChild == null))
            return true;

        return false;
    }

    //
    // Merge down traverses down the tree and deletes leaf nodes so that the parent can be rendered instead
    //
    void MergeDown(TriTreeNode tri)
    {
        // If we are a leaf node, then we cannot merge
        if (tri.LeftChild == null)
            return;

        // Can we merge?
        if (Mergable(tri))
        {
            // If we have no base neighbor, then we are at an edge and can merge
            if (tri.BaseNeighbor == null)
            {
                Merge(tri);
            }
            else
            {
                // Else we must be a diamond to merge, make sure the neighbor is merged before merging the self
                if (Mergable(tri.BaseNeighbor))
                {
                    Merge(tri.BaseNeighbor);
                    Merge(tri);
                    return;
                }

                return;
            }
            return;
        }

        // Recursively merge all children
        MergeDown(tri.LeftChild);
        MergeDown(tri.RightChild);
    }
};