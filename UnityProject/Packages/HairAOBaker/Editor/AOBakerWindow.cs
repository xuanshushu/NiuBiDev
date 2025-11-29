using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class AOBakerWindow : EditorWindow
{
    
    [SerializeField, Range(1, 256)]
    private int rayCount = 64;            // 每像素射线数量（默认 64）

    [SerializeField]
    private float rayMaxDistance = 1.0f;  // AO 最大距离（世界空间）
    
    [SerializeField]
    private Renderer targetMeshRenderer;
    
    private Mesh targetMesh;

    private enum UVChannel
    {
        UV0 = 0,
        UV1 = 1,
    }
    private enum TextureResolution
    {
        X1024 = 1024,
        X2048 = 2048,
        X4096 = 4096
    }

    [SerializeField]
    private UVChannel mainTexUV = UVChannel.UV0;   // 主贴图 UV 来源

    [SerializeField]
    private UVChannel bakeUV = UVChannel.UV1;      // 烘焙 AO 使用的 UV
    
    [SerializeField]
    private Texture2D mainTexture;          // 用户提供的主贴图（有 Alpha）

    [SerializeField, Range(0f, 1f)]
    private float mainAlphaThreshold = 0.5f; // A 通道阈值

    
    private TextureResolution targetResolution = TextureResolution.X1024;
    
    

    /// <summary>
    /// 输出贴图（Texture2D 资产）
    /// </summary>
    [SerializeField]
    private Texture2D userSelectedTexture;

    /// <summary>
    /// 输出贴图在工程里的路径（Assets/...）
    /// </summary>
    private string outputTexturePath;

    [MenuItem("Tools/AO Baker Window")]
    public static void ShowWindow()
    {
        var window = GetWindow<AOBakerWindow>("AO Baker");
        window.Show();
    }
    
    private ComputeShader aoBakeCS;

    private void OnGUI()
    {
        EditorGUILayout.LabelField("AO Baker", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.LabelField("AO Settings", EditorStyles.boldLabel);
        rayCount = EditorGUILayout.IntSlider("Ray Count", rayCount, 1, 256);
        rayMaxDistance = EditorGUILayout.FloatField("Max Ray Distance", rayMaxDistance);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        // 选择 Mesh
        targetMeshRenderer = (Renderer)EditorGUILayout.ObjectField("Mesh", targetMeshRenderer, typeof(Renderer), true);
        if (EditorGUI.EndChangeCheck())
        {
            if (targetMeshRenderer is MeshRenderer mr)
            {
                targetMesh = mr.gameObject.GetComponent<MeshFilter>().sharedMesh;
            }
            else if (targetMeshRenderer is SkinnedMeshRenderer smr)
            {
                targetMesh = smr.sharedMesh;
            }
        }
        
        EditorGUILayout.Space();

        // 主贴图 UV 通道
        mainTexUV = (UVChannel)EditorGUILayout.EnumPopup("Main Tex UV", mainTexUV);
        
        // 主贴图与 Alpha 阈值
        EditorGUILayout.LabelField("Main Texture (for alpha test)", EditorStyles.boldLabel);
        mainTexture = (Texture2D)EditorGUILayout.ObjectField("Main Texture", mainTexture, typeof(Texture2D), false);
        mainAlphaThreshold = EditorGUILayout.Slider("Main Alpha Threshold", mainAlphaThreshold, 0f, 1f);

        EditorGUILayout.Space();

        // 烘焙 UV 通道
        bakeUV = (UVChannel)EditorGUILayout.EnumPopup("Bake UV", bakeUV);
        
        targetResolution = (TextureResolution)EditorGUILayout.EnumPopup("Target Resolution", targetResolution);

        EditorGUILayout.Space();

        // 输出贴图的 Object + 浏览按钮
        EditorGUILayout.LabelField("Output Texture", EditorStyles.boldLabel);

        userSelectedTexture = (Texture2D)EditorGUILayout.ObjectField("Texture Asset", userSelectedTexture, typeof(Texture2D), false);

        // 显示当前路径
        if (!string.IsNullOrEmpty(outputTexturePath))
        {
            EditorGUILayout.LabelField("Path:", outputTexturePath);
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(targetMesh == null))
        {
            if (GUILayout.Button("开始烘焙", GUILayout.Height(32)))
            {
                StartBakeButtonClicked();
            }
        }
    }

    private Texture2D bakeTexture;

    void EnsureBakeTexture(int size)
    {
        if (bakeTexture == null ||
            bakeTexture.width != size ||
            bakeTexture.height != size)
        {
            bakeTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            bakeTexture.name = "AOBaker_Temp";
        }
    }
    
    
    /// <summary>
    /// 确保我们有一个合法的 TGA 输出路径：
    /// - 如果用户拖了一个 TGA 贴图进来，就用这张图对应的文件路径；
    /// - 否则弹出保存窗口，让用户选择/创建一个新的 TGA 文件。
    /// 返回 true 表示已经准备好 outputTexturePath 和 outputTexture。
    /// </summary>
    private bool EnsureOutputPathAndTexture()
    {
        int size = (int)targetResolution;

        // 1）用户在面板上拖了一个 Texture2D 进来
        if (userSelectedTexture != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(userSelectedTexture);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string ext = Path.GetExtension(assetPath).ToLowerInvariant();
                if (ext == ".tga")
                {
                    // 将 Assets 相对路径转换为绝对路径，后面用 File.WriteAllBytes 覆盖这个文件
                    string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
                    string fullPath = Path.Combine(projectRoot, assetPath);
                    fullPath = fullPath.Replace("\\", "/");

                    outputTexturePath = fullPath;
                    
                    return true;
                }
            }

            // 有贴图但不是 TGA 或没有 Asset 路径：不合法，走创建新 TGA 的流程
            Debug.LogWarning("当前拖入的贴图不是 TGA 文件，将重新选择输出路径并创建新的 TGA 文件。");
        }

        // 2）没有合法的 TGA 贴图 -> 弹出保存面板，让用户选一个 .tga 文件
        string defaultName = targetMesh != null ? targetMesh.name + "_AO" : "AOTexture";

        string path = EditorUtility.SaveFilePanel(
            "选择 AO 贴图保存位置（TGA）",
            Application.dataPath,
            defaultName,
            "tga"
        );

        if (string.IsNullOrEmpty(path))
        {
            // 用户取消
            return false;
        }

        // 记录路径，并创建一张内存中的临时贴图
        outputTexturePath = path;

        return true;
    }

    /// <summary>
    /// 点击“开始烘焙”按钮时的行为：
    /// 如果没有设置贴图位置，就弹出保存对话框，并新建贴图 Asset；
    /// 然后再进入真正的烘焙逻辑。
    /// </summary>
    private void StartBakeButtonClicked()
    {
        // 先确保我们有合法的 TGA 路径和一张用于烘焙的临时 Texture2D
        if (!EnsureOutputPathAndTexture())
        {
            // 用户取消了保存对话框
            return;
        }

        EnsureBakeTexture((int)targetResolution);
        
        if (!aoBakeCS)
        {
            aoBakeCS = AssetDatabase.LoadAssetAtPath<ComputeShader>("Packages/com.xuanxuan.hair_baker/Editor/HairAOBaker.compute");
            if (!aoBakeCS)
            {
                Debug.LogError("找不到 ComputeShader：Packages/com.xuanxuan.hair_baker/Editor/HairAOBaker.compute");
                return;
            }
        }

        // 真正的烘焙在这里调用
        BakeAO();
    }

    /// <summary>
    /// 真正的 AO 烘焙逻辑（这里先占位，后面可以写 ComputeShader 版本）
    /// </summary>
    private void BakeAO()
    {
        int mainUVIndex = (int)mainTexUV;  // 0 => UV, 1 => UV2
        int bakeUVIndex = (int)bakeUV;

        Debug.LogFormat(
            "开始 AO 烘焙：\nMesh: {0}\nMainTex UV: {1}\nBake UV: {2}\nOutput: {3}\n",
            targetMesh != null ? targetMesh.name : "null",
            mainUVIndex + 1,
            bakeUVIndex + 1,
            outputTexturePath
        );

        // ====== 在这里填你的 ComputeShader 烘焙逻辑 ======
        // 例如：
        // 1. 创建临时 RT / Buffer
        // 2. 传 Mesh 顶点 + UV + 索引到 ComputeShader
        // 3. 在 CS 里根据 bakeUVIndex 取不同 UV
        // 4. 写回 outputTexture 的像素（可以用 Texture2D.SetPixels + Encode 等）
        // ===============================================

        // 占位测试：把贴图清成灰色，表示“有结果”
        if (DispatchBaker())
        {
            Debug.Log("AO 烘焙完成（当前只是占位填充）。");
            // EditorUtility.SetDirty(outputTexture);
            // AssetDatabase.SaveAssets();
        }


    }
    
    // ===== Uniform Grid Buffers =====
    ComputeBuffer gridCellBuffer;       // each cell: start + count
    ComputeBuffer gridTriIndexBuffer;   // flattened triangle list
    
    struct GridCell
    {
        public int start;
        public int count;
    }

    struct GridInfo
    {
        public Vector3 min;
        public Vector3 invCellSize;
        public Vector3Int res;
    }
    
    Vector3Int WorldToCell(Vector3 p, Vector3 min, Vector3 invCellSize, Vector3Int res)
    {
        Vector3 f = p - min;
        int x = Mathf.Clamp((int)(f.x * invCellSize.x), 0, res.x - 1);
        int y = Mathf.Clamp((int)(f.y * invCellSize.y), 0, res.y - 1);
        int z = Mathf.Clamp((int)(f.z * invCellSize.z), 0, res.z - 1);
        return new Vector3Int(x,y,z);
    }

    GridInfo BuildUniformGrid(Vector3[] verticesWS, int[] indices)
    {
        // 1. Compute AABB
        Bounds bounds = new Bounds(verticesWS[0], Vector3.zero);
        for (int i = 1; i < verticesWS.Length; i++)
            bounds.Encapsulate(verticesWS[i]);

        Vector3 min = bounds.min;
        Vector3 size = bounds.size;

        // 2. Grid resolution
        int gridSize = 32; // can expose to UI later
        Vector3Int gridRes = new Vector3Int(gridSize, gridSize, gridSize);

        // 3. Compute voxel size
        Vector3 cellSize = size / gridSize;
        Vector3 invCellSize = new Vector3(
            1f / cellSize.x,
            1f / cellSize.y,
            1f / cellSize.z
        );

        int totalCells = gridSize * gridSize * gridSize;

        // 4. Allocate list for each cell
        List<int>[] cellTris = new List<int>[totalCells];
        for (int i = 0; i < totalCells; i++)
            cellTris[i] = new List<int>();

        // 5. For each triangle, find which cells it touches
        int triCount = indices.Length / 3;

        for (int t = 0; t < triCount; t++)
        {
            int i0 = indices[t * 3 + 0];
            int i1 = indices[t * 3 + 1];
            int i2 = indices[t * 3 + 2];

            Vector3 p0 = verticesWS[i0];
            Vector3 p1 = verticesWS[i1];
            Vector3 p2 = verticesWS[i2];

            // Triangle AABB
            Vector3 triMin = Vector3.Min(p0, Vector3.Min(p1, p2));
            Vector3 triMax = Vector3.Max(p0, Vector3.Max(p1, p2));

            Vector3Int cMin = WorldToCell(triMin, min, invCellSize, gridRes);
            Vector3Int cMax = WorldToCell(triMax, min, invCellSize, gridRes);

            for (int z = cMin.z; z <= cMax.z; z++)
            for (int y = cMin.y; y <= cMax.y; y++)
            for (int x = cMin.x; x <= cMax.x; x++)
            {
                int cellIndex = x + y * gridRes.x + z * gridRes.x * gridRes.y;
                cellTris[cellIndex].Add(t);
            }
        }

        // 6. Flatten tri lists
        List<int> flat = new List<int>();
        GridCell[] gridCells = new GridCell[totalCells];

        int offset = 0;
        for (int c = 0; c < totalCells; c++)
        {
            gridCells[c].start = offset;
            gridCells[c].count = cellTris[c].Count;
            flat.AddRange(cellTris[c]);
            offset += cellTris[c].Count;
        }

        // 7. Upload buffer

        gridCellBuffer?.Release();
        gridTriIndexBuffer?.Release();

        gridCellBuffer = new ComputeBuffer(totalCells, sizeof(int) * 2);
        gridTriIndexBuffer = new ComputeBuffer(flat.Count, sizeof(int));

        gridCellBuffer.SetData(gridCells);
        gridTriIndexBuffer.SetData(flat.ToArray());

        GridInfo info = new GridInfo
        {
            min = min,
            invCellSize = invCellSize,
            res = gridRes
        };
        return info;

    }




    bool DispatchBaker()
    {
        
        
        Vector3[] vertices = targetMesh.vertices;
        Vector3[] normals = targetMesh.normals;
        targetMesh.RecalculateNormals();
        if (normals == null || normals.Length == 0)
        {
            normals = targetMesh.normals;
        }

        int vertexCount = normals.Length;
        if (vertexCount == 0)
        {
            Debug.LogError("Mesh 没有顶点。");
            return false;
        }

        int bakeUVIndex = (int)bakeUV;      // 用于烘焙 AO 贴图的 UV（raster）
        int mainUVIndex = (int)mainTexUV;   // 用于采样 MainTex 的 UV（ray hit）

        Vector2[] bakeUVs = null;
        Vector2[] mainUVs = null;

        // Bake UV
        if (bakeUVIndex == 0)
            bakeUVs = targetMesh.uv;
        else if (bakeUVIndex == 1)
            bakeUVs = targetMesh.uv2;

        // Main UV
        if (mainUVIndex == 0)
            mainUVs = targetMesh.uv;
        else if (mainUVIndex == 1)
            mainUVs = targetMesh.uv2;

        if (bakeUVs == null || bakeUVs.Length == 0)
        {
            Debug.LogError($"Mesh 没有 UV{bakeUVIndex + 1} 数据（用于 Bake）。");
            return false;
        }
        if (mainUVs == null || mainUVs.Length == 0)
        {
            Debug.LogWarning($"Mesh 没有 UV{mainUVIndex + 1} 数据（用于 MainTex 采样），将不使用 Alpha Test。");
        }

        int[] indices = targetMesh.triangles;
        if (indices == null || indices.Length == 0)
        {
            Debug.LogError("Mesh 没有三角形索引。");
            return false;
        }
        

        int triangleCount = indices.Length / 3;
        


        // === 2. 创建 ComputeBuffer ===

        ComputeBuffer normalBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
        ComputeBuffer bakeUVBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 2);
        ComputeBuffer indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));
        ComputeBuffer positionBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
        ComputeBuffer mainUVBuffer = null;
        if (mainUVs != null && mainUVs.Length == vertexCount)
        {
            mainUVBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 2);
            mainUVBuffer.SetData(mainUVs);
        }

        normalBuffer.SetData(normals);
        bakeUVBuffer.SetData(bakeUVs);
        indexBuffer.SetData(indices);
        positionBuffer.SetData(vertices);

        // === 3. 创建临时 RenderTexture 作为输出 ===

        int width = (int)targetResolution;
        int height = (int)targetResolution;

        RenderTexture rtMain = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        rtMain.enableRandomWrite = true;
        rtMain.Create();

        // 清空 RT
        Graphics.SetRenderTarget(rtMain);
        GL.Clear(true, true, new Color(0,0,0,0));
        Graphics.SetRenderTarget(null);

        // === 4. 设置 ComputeShader 参数并 Dispatch ===

        int rasterKernel = aoBakeCS.FindKernel("HairRaster");
        int aoRayTestKernel = aoBakeCS.FindKernel("AORayTest");
        int textureDilateKernel = aoBakeCS.FindKernel("HairTextureDilate");

        aoBakeCS.SetInts("_TextureSize", new int[] { width, height });
        aoBakeCS.SetInt("_TriangleCount", triangleCount);
        
        aoBakeCS.SetInt("_RayCount", rayCount);
        aoBakeCS.SetFloat("_RayMaxDistance", rayMaxDistance);
        
        aoBakeCS.SetBuffer(rasterKernel, "_VertexNormals", normalBuffer);
        aoBakeCS.SetBuffer(rasterKernel, "_VertexPoses", positionBuffer);
        aoBakeCS.SetBuffer(rasterKernel, "_BakeUVs", bakeUVBuffer);
        aoBakeCS.SetBuffer(rasterKernel, "_Indices", indexBuffer);


        
        
        int pixelCount = width * height;
        ComputeBuffer pixelPosesBuffer = new ComputeBuffer(pixelCount, sizeof(float) * 3);
        ComputeBuffer pixelNormalsBuffer = new ComputeBuffer(pixelCount, sizeof(float) * 3);
        ComputeBuffer pixelValidMaskBuffer = new ComputeBuffer(pixelCount, sizeof(uint));
        aoBakeCS.SetBuffer(rasterKernel, "_PixelPoses", pixelPosesBuffer);
        aoBakeCS.SetBuffer(rasterKernel, "_PixelNormals", pixelNormalsBuffer);
        aoBakeCS.SetBuffer(rasterKernel, "_PixelValidMask", pixelValidMaskBuffer);
        
        int threadGroupSize = 64; // 对应 [numthreads(64,1,1)]
        int dispatchCount = Mathf.CeilToInt(triangleCount / (float)threadGroupSize);
        aoBakeCS.Dispatch(rasterKernel, dispatchCount, 1, 1);
        
        GridInfo info = BuildUniformGrid(vertices,indices);
        aoBakeCS.SetVector("_GridMin", info.min);
        aoBakeCS.SetVector("_GridInvCellSize", info.invCellSize);
        aoBakeCS.SetInts("_GridResolution", info.res.x, info.res.y, info.res.z);;
        aoBakeCS.SetBuffer(aoRayTestKernel, "_GridCells", gridCellBuffer);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_GridTriIndices", gridTriIndexBuffer);
        
        
        aoBakeCS.SetBuffer(aoRayTestKernel, "_VertexPoses", positionBuffer);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_BakeUVs", bakeUVBuffer);
        if (mainUVBuffer != null)
            aoBakeCS.SetBuffer(aoRayTestKernel, "_MainUVs", mainUVBuffer);
        // MainTex alpha test 开关 + 阈值
        if (mainTexture != null && mainUVBuffer != null)
        {
            aoBakeCS.SetTexture(aoRayTestKernel, "_MainTex", mainTexture);
            aoBakeCS.SetInt("_UseMainTexAlpha", 1);
        }
        else
        {
            aoBakeCS.SetInt("_UseMainTexAlpha", 0);
        }
        aoBakeCS.SetFloat("_MainAlphaThreshold", mainAlphaThreshold);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_Indices", indexBuffer);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_PixelPoses", pixelPosesBuffer);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_PixelNormals", pixelNormalsBuffer);
        aoBakeCS.SetBuffer(aoRayTestKernel, "_PixelValidMask", pixelValidMaskBuffer);
        aoBakeCS.SetTexture(aoRayTestKernel,"_AOOutput",rtMain);
        
        int groupX = Mathf.CeilToInt(width / 8.0f);
        int groupY = Mathf.CeilToInt(height / 8.0f);
        aoBakeCS.Dispatch(aoRayTestKernel, groupX, groupY, 1);

        
        
        // === 5. 扩边：用两个 RT ping-pong 几次 ===

        int dilateIterations = 4;   // 你可以调这个值
        int dilateRadius     = 1;   // 每次扩 1 像素，迭代 4 次就是大约 4 像素
        
        RenderTexture rtA = rtMain;
        RenderTexture rtB = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        rtB.enableRandomWrite = true;
        rtB.Create();
        
        int kernelDilate = aoBakeCS.FindKernel("HairTextureDilate");
        
        aoBakeCS.SetInt("_DilateRadius", dilateRadius);
        
        
        for (int i = 0; i < dilateIterations; i++)
        {
            // Source 用作只读纹理，Dest 用作可写纹理
            aoBakeCS.SetTexture(kernelDilate, "_SourceTex", rtA);
            aoBakeCS.SetTexture(kernelDilate, "_DestTex",   rtB);
        
            aoBakeCS.Dispatch(kernelDilate, groupX, groupY, 1);
        
            // 交换 rtA / rtB，下一轮继续
            var tmp = rtA;
            rtA = rtB;
            rtB = tmp;
        }

        RenderTexture finalRT = rtA;

        // === 5. 把 RenderTexture 拷回 Texture2D（内存里的预览） ===

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = finalRT;


        bakeTexture.ReadPixels(new Rect(0,0,width,height),0,0);
        bakeTexture.Apply();

        RenderTexture.active = prev;

// === 6. 把 Texture2D 编码成 TGA 并写到文件 ===

        byte[] bytes = bakeTexture.EncodeToTGA();
        File.WriteAllBytes(outputTexturePath, bytes);
        AssetDatabase.Refresh();
        
        
        // === 8. 清理 ===

        normalBuffer.Release();
        bakeUVBuffer.Release();
        if (mainUVBuffer != null) mainUVBuffer.Release();
        indexBuffer.Release();
        rtMain.Release();
        rtB.Release();
        positionBuffer.Release();
        pixelPosesBuffer.Release();
        pixelNormalsBuffer.Release();
        
        return true;

    }
}
