using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//
// Store the triangle node data
//
public class TriTreeNode
{
    public TriTreeNode LeftChild = null;
    public TriTreeNode RightChild = null;

    public TriTreeNode BaseNeighbor = null;
    public TriTreeNode LeftNeighbor = null;
    public TriTreeNode RightNeighbor = null;

    public TriTreeNode Parent = null;

    public int indexTri = -1;
    public bool isRendered = false;
    public bool isTessellated = false;
}
