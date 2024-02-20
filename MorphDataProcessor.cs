#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO.Compression;
using Unity.Jobs;
using Unity.Collections;

public class MorphDataProcessor : EditorWindow
{
    [SerializeField] private List<GameObject> mainObjects = new List<GameObject>();
    [SerializeField] private List<VisualPiece> visualPieces = new List<VisualPiece>();
    [SerializeField] private List<GameObject> globalTargetsAndSources = new List<GameObject>();
    [SerializeField] private VisualPiece rawMaterialVisual;
    private List<int> unusedMorphedVertices;
    Dictionary<int, List<int>> adjacentVerticesOfRawMaterial;

    private SerializedObject serializedObject;
    private SerializedProperty mainObjectsProperty;
    private SerializedProperty visualPiecesDataProperty;
    private SerializedProperty globalTargetsAndSourcesProperty;
    private SerializedProperty rawMaterialVisualProperty;

    [MenuItem("Tools/Morph Data Processor")]
    public static void ShowWindow()
    {
        GetWindow<MorphDataProcessor>("Morph Data Processor");
    }

    void OnEnable()
    {
        serializedObject = new SerializedObject(this);
        mainObjectsProperty = serializedObject.FindProperty("mainObjects");
        visualPiecesDataProperty = serializedObject.FindProperty("visualPieces");
        globalTargetsAndSourcesProperty = serializedObject.FindProperty("globalTargetsAndSources");
        rawMaterialVisualProperty = serializedObject.FindProperty("rawMaterialVisual");
    }

    void OnGUI()
    {
        if (serializedObject == null || mainObjectsProperty == null || visualPiecesDataProperty == null)
        {
            EditorGUILayout.HelpBox("Serialized properties not initialized!", MessageType.Error);
            return;
        }

        serializedObject.Update();

        EditorGUILayout.PropertyField(mainObjectsProperty, true);
        EditorGUILayout.PropertyField(visualPiecesDataProperty, true);
        EditorGUILayout.PropertyField(globalTargetsAndSourcesProperty, true);
        EditorGUILayout.PropertyField(rawMaterialVisualProperty, true);

        if (GUILayout.Button("Process Objects"))
        {
            ProcessObjects();
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void ProcessObjects()
    {
        BinaryFormatter bf = new BinaryFormatter();
        string path = "Assets/Resources/ClosestPointIndicesData/";
        string dataFileName = "MorphData.bytes";  // single file for all data

        List<ClosestPointData> dataList = new List<ClosestPointData>();

        foreach (GameObject mainObject in mainObjects)
        {
            VisualPiece[] visualPieces = mainObject.GetComponentsInChildren<VisualPiece>();

            foreach (VisualPiece visualPiece in visualPieces)
            {
                int[] closestPointIndices = CalculateClosestPointIndices(rawMaterialVisual, visualPiece);

                if (closestPointIndices != null)
                {
                    byte[] compressedData = CompressData(closestPointIndices);
                    ClosestPointData data = new ClosestPointData
                    {
                        TargetName = visualPiece.name,
                        CompressedData = compressedData
                    };

                    dataList.Add(data);
                }
            }
        }

        using (FileStream file = File.Create(path + dataFileName))
        {
            bf.Serialize(file, dataList);
        }

        UnityEditor.AssetDatabase.Refresh();
    }

    private int[] CalculateClosestPointIndices(VisualPiece rawMaterial, VisualPiece target)
    {
        Mesh rawMaterialMesh = MeshCalculations.GetMeshFromObject(rawMaterial.gameObject);
        Vector3[] rawMaterialVertices = rawMaterialMesh.vertices;

        Mesh targetMesh = MeshCalculations.GetMeshFromObject(target.gameObject);
        Vector3[] targetVertices = targetMesh.vertices;

        int[] closestPointIndices = CalculateCorrespondingPoints(rawMaterialVertices, targetVertices);
        return closestPointIndices;
    }

    private int[] CalculateCorrespondingPoints(Vector3[] sourceVertices, Vector3[] targetVertices)
    {
        sourceVertices = MeshCalculations.AlignMeshes(targetVertices, sourceVertices);
        Vector3 targetCenter = MeshCalculations.CalculateCentroid(targetVertices);

        NativeArray<Vector3> sourceVerticesNative = new NativeArray<Vector3>(sourceVertices, Allocator.TempJob);
        NativeArray<Vector3> targetVerticesNative = new NativeArray<Vector3>(targetVertices, Allocator.TempJob);
        NativeArray<int> correspondingIndicesNative = new NativeArray<int>(sourceVertices.Length, Allocator.TempJob);

        CalculateCorrespondingPointsJob job = new CalculateCorrespondingPointsJob
        {
            SourceVertices = sourceVerticesNative,
            TargetVertices = targetVerticesNative,
            TargetCenter = targetCenter,
            CorrespondingIndices = correspondingIndicesNative
        };

        JobHandle handle = job.Schedule(sourceVertices.Length, 64);
        handle.Complete();

        int[] correspondingIndices = correspondingIndicesNative.ToArray();

        sourceVerticesNative.Dispose();
        targetVerticesNative.Dispose();
        correspondingIndicesNative.Dispose();

        return correspondingIndices;
    }


    private byte[] CompressData(int[] data)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            using (GZipStream compressionStream = new GZipStream(ms, CompressionMode.Compress))
            {
                BinaryWriter writer = new BinaryWriter(compressionStream);
                writer.Write(data.Length);
                foreach (int i in data)
                {
                    writer.Write(i);
                }
            }
            return ms.ToArray();
        }
    }
}

public struct CalculateCorrespondingPointsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Vector3> SourceVertices;
    [ReadOnly] public NativeArray<Vector3> TargetVertices;
    [ReadOnly] public Vector3 TargetCenter;
    public NativeArray<int> CorrespondingIndices;

    public void Execute(int index)
    {
        float minDistance = float.MaxValue;
        int minIndex = -1;

        for (int j = 0; j < TargetVertices.Length; j++)
        {
            float distanceToTarget = Vector3.Distance(SourceVertices[index], TargetVertices[j]);
            float distanceToCenter = Vector3.Distance(TargetVertices[j], TargetCenter);

            float totalDistance = distanceToTarget + distanceToCenter;

            if (totalDistance < minDistance)
            {
                minDistance = totalDistance;
                minIndex = j;
            }
        }

        CorrespondingIndices[index] = minIndex;
    }
}
#endif
