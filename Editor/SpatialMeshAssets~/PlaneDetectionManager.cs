#if ENABLE_PICO_XR_SDK
/*******************************************************************************
Copyright © 2015-2022 PICO Technology Co., Ltd.All rights reserved.  

NOTICE：All information contained herein is, and remains the property of 
PICO Technology Co., Ltd. The intellectual and technical concepts 
contained herein are proprietary to PICO Technology Co., Ltd. and may be 
covered by patents, patents in process, and are protected by trade secret or 
copyright law. Dissemination of this information or reproduction of this 
material is strictly forbidden unless prior written permission is obtained from
PICO Technology Co., Ltd. 
*******************************************************************************/
// PlaneDetectionManager is the Plane-Detection sibling of SpatialMeshManager.
// It reuses SpatialMeshManager's render pipeline VERBATIM -- an object pool of
// wireframe mesh instances, a per-frame throttle, the global _TargetPosition
// camera feed and the per-instance _StartTime feed that drive the
// Custom/TriangleFadeOutFromCenter fade shader -- so detected planes render
// with the SAME visual as the spatial mesh.
//
// The ONLY differences from SpatialMeshManager are the data source and how it
// is started:
//   * data source : PXR_Manager.PlaneDetectionDataUpdated (List<PxrPlaneData>)
//                   instead of PXR_Manager.SpatialMeshDataUpdated.
//   * start       : PXR_MixedReality.StartSenseDataProvider(PlaneDetection).
//                   There is NO planeSubsystem accessor (unlike meshSubsystem);
//                   PXR_Manager itself drives QueryPlaneAnchor on every
//                   SenseDataUpdated for the PlaneDetection provider and fires
//                   the event, so this driver only starts the provider and
//                   subscribes.
//   * state       : the plane data stream also emits MeshChangeState.Unchanged
//                   (the mesh stream does not); it is handled as a no-op drop.
//
// PxrPlaneData shares uuid / state / position / rotation / indices / vertices
// with PxrSpatialMeshInfo, so the geometry-build code is identical, including
// the world-space vertex bake (rotation * v + position) that keeps the object
// at the origin and lets the world-space _TargetPosition line up. Unlike the
// SDK's PXR_PlaneDetectionManager, this driver does NOT overwrite the material
// color per semantic label -- that would clobber the wireframe fade material.
using System;
using System.Collections.Generic;
using System.Linq;
using ByteDance.PICO.XR;
using UnityEngine;

public class PlaneDetectionManager : MonoBehaviour
{
    public static PlaneDetectionManager Instance { get; private set; }
    [Header("通用配置")]
    [SerializeField] private int maxRenderPerFrame = 100; // 每帧最大渲染数量
    [SerializeField] private int meshAmount = 200;
    [Header("材质与容器")]
    [SerializeField] private Transform meshContainer; // plane容器
    [SerializeField] private GameObject meshPrefab; // 模版
    [SerializeField] private Material wireframeMaterial;
    [SerializeField] private Material transparentMaterial;
    private readonly Dictionary<Guid, GameObject> planeList = new();
    private readonly Dictionary<Guid, PxrPlaneData> needUpdatePlaneList = new();
    private readonly Queue<GameObject> pool = new();
    private Mesh mesh;
    private Transform _camera;
    private readonly object listLock = new();
    private bool isStopUpdatePlane = false;
    private bool subscribed = false;
    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            Debug.LogError($"该单例存在多个实例！");
        }
        else
        {
            Destroy(Instance);
            Instance = this;
        }
    }
    void Start()
    {
        _camera = Camera.main.transform;
        InitSystem();
    }
    public void StopUpdate()
    {
        StopUpdatePlane();
    }
    private void InitSystem()
    {
        // Plane detection has no XR subsystem accessor (unlike meshSubsystem);
        // start the sense-data provider directly. PXR_Manager drives the query
        // loop and fires PlaneDetectionDataUpdated.
        PXR_MixedReality.StartSenseDataProvider(PxrSenseDataProviderType.PlaneDetection);
        PXR_Manager.PlaneDetectionDataUpdated += PXR_OnPlaneDetectionDataUpdated;
        subscribed = true;
        for (var i = 0; i < meshAmount; i++)
        {
            var plane = Instantiate(meshPrefab, meshContainer);
            pool.Enqueue(plane);
            plane.SetActive(false);
        }
    }
    void Update()
    {
        if (isStopUpdatePlane) return;
        Shader.SetGlobalVector("_TargetPosition", _camera.position);
        if (needUpdatePlaneList.Count > 0)
        {
            lock (listLock)
            {
                var keysToRemove = needUpdatePlaneList.Keys.Take(maxRenderPerFrame).ToList();
                foreach (var key in keysToRemove)
                {
                    PlaneUpdateQueue(needUpdatePlaneList[key]);
                }
                // 删除这些键对应的记录
                foreach (var key in keysToRemove)
                {
                    needUpdatePlaneList.Remove(key);
                }
            }
        }
    }

    private void PlaneUpdateQueue(PxrPlaneData pxrPlaneData)
    {
        var id = pxrPlaneData.uuid;
        switch (pxrPlaneData.state)
        {
            case MeshChangeState.Added:
            case MeshChangeState.Updated:
                if (planeList.ContainsKey(id))
                {
                    CreateMesh(pxrPlaneData, planeList[id]);
                }
                else
                {
                    GameObject newPlane = GetPlaneFromPool();
                    planeList.Add(id, newPlane);
                    CreateMesh(pxrPlaneData, newPlane);
                }
                break;
            case MeshChangeState.Removed:
                if (planeList.ContainsKey(id))
                {
                    SetPlaneToPool(planeList[id]);
                    planeList.Remove(id);
                }
                break;
            default:
                break;
        }
    }

    private GameObject GetPlaneFromPool()
    {
        GameObject plane;
        if (pool.Count > 0)
        {
            plane = pool.Dequeue();
            plane.SetActive(true);
        }
        else
        {
            plane = Instantiate(meshPrefab, meshContainer);
            pool.Enqueue(plane);
        }
        var renderer = plane.GetComponent<MeshRenderer>();
        MaterialPropertyBlock props = new();
        props.SetFloat("_StartTime", Time.time);
        renderer.SetPropertyBlock(props);
        return plane;
    }
    private void SetPlaneToPool(GameObject plane)
    {
        plane.SetActive(false);
        if (pool.Count > meshAmount)
        {
            Destroy(plane);
        }
        else
        {
            pool.Enqueue(plane);
        }
    }
    private void PXR_OnPlaneDetectionDataUpdated(List<PxrPlaneData> list)
    {
        lock (listLock)
        {
            foreach (var item in list)
            {
                if (needUpdatePlaneList.ContainsKey(item.uuid))
                {
                    switch (item.state)
                    {
                        case MeshChangeState.Added:
                        case MeshChangeState.Updated:
                            needUpdatePlaneList[item.uuid] = item;
                            break;
                        case MeshChangeState.Removed:
                            needUpdatePlaneList.Remove(item.uuid);
                            break;
                        case MeshChangeState.Unchanged:
                            // Plane stream emits Unchanged; nothing to redraw.
                            needUpdatePlaneList.Remove(item.uuid);
                            break;
                        default:
                            break;
                    }
                }
                else if (item.state != MeshChangeState.Unchanged)
                {
                    needUpdatePlaneList.Add(item.uuid, item);
                }
            }
        }
    }
    private void CreateMesh(PxrPlaneData block, GameObject planeGameObject)
    {
        var meshFilter = planeGameObject.GetComponentInChildren<MeshFilter>();
        var meshCollider = planeGameObject.GetComponentInChildren<MeshCollider>();
        if (meshFilter.mesh == null)
        {
            mesh = new Mesh();
            mesh.Clear();
            mesh.MarkDynamic();
        }
        else
        {
            mesh = meshFilter.mesh;
            mesh.Clear();
        }
        var vertices = new List<Vector3>();
        for (var i = 0; i < block.vertices.Length; i++)
        {
            vertices.Add(block.rotation * block.vertices[i] + block.position);
        }
        mesh.SetVertices(vertices);
        mesh.SetTriangles(block.indices, 0);
        mesh.RecalculateNormals();
        meshFilter.mesh = mesh;
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = mesh;
        }
    }
    private void StopUpdatePlane()
    {
        isStopUpdatePlane = true;
#if !UNITY_EDITOR
        if (subscribed) PXR_Manager.PlaneDetectionDataUpdated -= PXR_OnPlaneDetectionDataUpdated;
#endif
    }
    private void OnDisable()
    {
        if (subscribed)
        {
            PXR_Manager.PlaneDetectionDataUpdated -= PXR_OnPlaneDetectionDataUpdated;
            subscribed = false;
        }
    }
}
#endif
