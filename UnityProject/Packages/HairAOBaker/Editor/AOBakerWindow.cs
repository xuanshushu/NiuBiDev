using UnityEngine;
using UnityEditor;
using System.IO;

public class AOBakerWindow : EditorWindow
{
    /// <summary>
    /// 要烘焙的 Mesh（这里先直接用 Mesh 资产，你也可以改成 MeshFilter / SkinnedMeshRenderer）
    /// </summary>
    [SerializeField]
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
    private Texture2D outputTexture;

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

    private void OnGUI()
    {
        EditorGUILayout.LabelField("AO Baker", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 选择 Mesh
        targetMesh = (Mesh)EditorGUILayout.ObjectField("Mesh", targetMesh, typeof(Mesh), false);

        EditorGUILayout.Space();

        // 主贴图 UV 通道
        mainTexUV = (UVChannel)EditorGUILayout.EnumPopup("Main Tex UV", mainTexUV);

        // 烘焙 UV 通道
        bakeUV = (UVChannel)EditorGUILayout.EnumPopup("Bake UV", bakeUV);
        
        targetResolution = (TextureResolution)EditorGUILayout.EnumPopup("Target Resolution", targetResolution);

        EditorGUILayout.Space();

        // 输出贴图的 Object + 浏览按钮
        EditorGUILayout.LabelField("Output Texture", EditorStyles.boldLabel);

        outputTexture = (Texture2D)EditorGUILayout.ObjectField("Texture Asset", outputTexture, typeof(Texture2D), false);

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

    /// <summary>
    /// 根据路径尝试加载已有贴图；没有的话新建一个 Texture2D 资产，并记录 path
    /// </summary>
    private void CreateOrLoadTextureAtPath(string path)
    {
        outputTexturePath = path;

        // 先尝试加载已有 Asset
        outputTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputTexturePath);
        if (outputTexture == null)
        {
            // 如果不存在，则新建一个 Texture2D 资产（默认 1024x1024，可后续在窗口加个分辨率选项）
            var tex = new Texture2D((int)targetResolution, (int)targetResolution, TextureFormat.RGBA32, false, true);
            tex.name = Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(tex, outputTexturePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            outputTexture = tex;
        }
    }

    /// <summary>
    /// 点击“开始烘焙”按钮时的行为：
    /// 如果没有设置贴图位置，就弹出保存对话框，并新建贴图 Asset；
    /// 然后再进入真正的烘焙逻辑。
    /// </summary>
    private void StartBakeButtonClicked()
    {
        // 没有指定输出贴图或路径 => 先弹出保存窗口
        if (outputTexture == null || string.IsNullOrEmpty(outputTexturePath))
        {
            string defaultName = targetMesh != null ? targetMesh.name + "_AO" : "AOTexture";

            string path = EditorUtility.SaveFilePanelInProject(
                "选择 AO 贴图保存位置",
                defaultName,
                "asset",
                "请选择 AO 贴图保存的位置（会创建/覆盖一个 Texture2D 资源）"
            );

            if (string.IsNullOrEmpty(path))
            {
                // 用户取消
                return;
            }

            CreateOrLoadTextureAtPath(path);
        }

        // 到这里一定有有效的 outputTexture 和 outputTexturePath
        if (outputTexture == null)
        {
            Debug.LogError("Output texture is null even after path selection.");
            return;
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
            "开始 AO 烘焙：\nMesh: {0}\nMainTex UV: {1}\nBake UV: {2}\nOutput: {3}\nPath: {4}",
            targetMesh != null ? targetMesh.name : "null",
            mainUVIndex + 1,
            bakeUVIndex + 1,
            outputTexture != null ? outputTexture.name : "null",
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
        var pixels = outputTexture.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(128, 128, 128, 255);
        }
        outputTexture.SetPixels32(pixels);
        outputTexture.Apply();

        EditorUtility.SetDirty(outputTexture);
        AssetDatabase.SaveAssets();

        Debug.Log("AO 烘焙完成（当前只是占位填充）。");
    }
}
