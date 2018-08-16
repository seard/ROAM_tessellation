using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using System.IO;
using UnityEngine.UI;
using System;

public class TESTINGSCRIPT : MonoBehaviour
{
    // Location of wanted highmap
    public string heightmapPath;

    // Use this for initialization
    void Start()
    {
        // Call to load the heightmap into memory
        Landscape.LoadHeightMap(heightmapPath);

        // Initiate the landscape and patches
        Landscape.Init();
    }

    void Update()
    {
        // Run the algorithm...
        Landscape.Reset();
        Landscape.Tessellate();
        Landscape.Render();
    }
}
