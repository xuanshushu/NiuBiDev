using UnityEngine;
using UnityEditor;
using System.IO;

public class AOBakerWindow : EditorWindow
{
    /// <summary>
    /// 要烘焙的 Mesh（这里先直接用 Mesh 资产，你也可以改成 MeshFilter / SkinnedMeshRenderer）
    /// </summary>
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
    
    


    bool DispatchBaker()
    {
        
        
        Vector3[] normals = targetMesh.normals;
        if (normals == null || normals.Length == 0)
        {
            targetMesh.RecalculateNormals();
            normals = targetMesh.normals;
        }

        int vertexCount = normals.Length;
        if (vertexCount == 0)
        {
            Debug.LogError("Mesh 没有顶点。");
            return false;
        }

        int bakeUVIndex = (int)bakeUV; // 0: UV, 1: UV2
        Vector2[] uvs = null;
        if (bakeUVIndex == 0)
            uvs = targetMesh.uv;
        else if (bakeUVIndex == 1)
            uvs = targetMesh.uv2;

        if (uvs == null || uvs.Length == 0)
        {
            Debug.LogError($"Mesh 没有 UV{bakeUVIndex + 1} 数据。");
            return false;
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
        ComputeBuffer uvBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 2);
        ComputeBuffer indexBuffer = new ComputeBuffer(indices.Length, sizeof(int));

        normalBuffer.SetData(normals);
        uvBuffer.SetData(uvs);
        indexBuffer.SetData(indices);

        // === 3. 创建临时 RenderTexture 作为输出 ===

        int width = (int)targetResolution;
        int height = (int)targetResolution;

        RenderTexture rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        rt.enableRandomWrite = true;
        rt.Create();

        // 清空 RT
        Graphics.SetRenderTarget(rt);
        GL.Clear(true, true, Color.black);
        Graphics.SetRenderTarget(null);

        // === 4. 设置 ComputeShader 参数并 Dispatch ===

        int kernel = aoBakeCS.FindKernel("HairAOBaker");

        aoBakeCS.SetBuffer(kernel, "_Normals", normalBuffer);
        aoBakeCS.SetBuffer(kernel, "_UVs", uvBuffer);
        aoBakeCS.SetBuffer(kernel, "_Indices", indexBuffer);

        aoBakeCS.SetTexture(kernel, "_OutputTex", rt);

        aoBakeCS.SetInt("_TriangleCount", triangleCount);
        aoBakeCS.SetInts("_TextureSize", new int[] { width, height });

        int threadGroupSize = 64; // 对应 [numthreads(64,1,1)]
        int dispatchCount = Mathf.CeilToInt(triangleCount / (float)threadGroupSize);

        aoBakeCS.Dispatch(kernel, dispatchCount, 1, 1);

        // === 5. 把 RenderTexture 拷回 Texture2D（内存里的预览） ===

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;


        bakeTexture.ReadPixels(new Rect(0,0,width,height),0,0);
        bakeTexture.Apply();

        RenderTexture.active = prev;

// === 6. 把 Texture2D 编码成 TGA 并写到文件 ===

        byte[] bytes = bakeTexture.EncodeToTGA();
        File.WriteAllBytes(outputTexturePath, bytes);
        AssetDatabase.Refresh();
        return true;

    }
}
