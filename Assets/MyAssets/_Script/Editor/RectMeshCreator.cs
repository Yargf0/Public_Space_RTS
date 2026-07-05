using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

public class RectMeshCreator : EditorWindow
{
    private float width = 1f;
    private float height = 2f;

    private string folderPath = "Assets/GeneratedMeshes";

    [MenuItem("Tools/Mesh/Rect Mesh Creator")]
    private static void OpenWindow()
    {
        GetWindow<RectMeshCreator>("Rect Mesh Creator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Rect Mesh Settings", EditorStyles.boldLabel);

        width = EditorGUILayout.FloatField("Width", width);
        height = EditorGUILayout.FloatField("Height", height);

        GUILayout.Space(8);

        string meshName = BuildMeshName();

        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.TextField("Mesh Name", meshName);
        EditorGUI.EndDisabledGroup();

        folderPath = EditorGUILayout.TextField("Folder Path", folderPath);

        GUILayout.Space(12);

        if (GUILayout.Button("Create Mesh Asset"))
        {
            CreateMeshAsset(meshName);
        }
    }

    private string BuildMeshName()
    {
        string w = width.ToString("0.###", CultureInfo.InvariantCulture);
        string h = height.ToString("0.###", CultureInfo.InvariantCulture);

        return $"RectQuad_{w}x{h}";
    }

    private void CreateMeshAsset(string meshName)
    {
        if (width <= 0f || height <= 0f)
        {
            Debug.LogError("Width and Height must be bigger than 0.");
            return;
        }

        if (!folderPath.StartsWith("Assets"))
        {
            Debug.LogError("Folder Path must start with Assets.");
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            AssetDatabase.Refresh();
        }

        Mesh mesh = CreateRectMesh(width, height);
        mesh.name = meshName;

        string assetPath = $"{folderPath}/{meshName}.asset";

        assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);

        AssetDatabase.CreateAsset(mesh, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"Mesh created!\n" +
            $"Path: {assetPath}\n" +
            $"Size: {width} x {height}"
        );

        EditorGUIUtility.PingObject(mesh);
    }

    private Mesh CreateRectMesh(float meshWidth, float meshHeight)
    {
        float halfWidth = meshWidth * 0.5f;
        float halfHeight = meshHeight * 0.5f;

        Mesh mesh = new Mesh();

        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth, -halfHeight, 0f),
            new Vector3(-halfWidth,  halfHeight, 0f),
            new Vector3( halfWidth,  halfHeight, 0f),
        };

        mesh.triangles = new int[]
        {
            0, 2, 1,
            2, 3, 1
        };

        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
        };

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}