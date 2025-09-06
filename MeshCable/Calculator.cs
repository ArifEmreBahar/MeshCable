using Photon.Pun;
using AEB.Photon;
using AEB.Utilities;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace AEB.Systems.Cable
{
    public class Calculator : Singleton<Calculator>
    {
        List<MeshCable> _meshCables = new List<MeshCable>();

        NativeArray<float3> _nativeVertices;
        NativeArray<CableData> _cableDataArray;
        NativeArray<float3> _reusableCableVertices;

        int _expectedPlayerCount;
        int _currentInstantiatedPlayers;
        bool _initialized;
        protected override bool ShouldPersist => false;

        struct CableData
        {
            public int Segments;
            public int RadialSegments;
            public float CableRadius;
            public float3 PointA;
            public float3 PointB;
            public float3 StartControlPoint;
            public float3 EndControlPoint;
            public quaternion LookRotation;
            public int VertexOffset;
        }

        #region Unity

        void Start()
        {
            if (PhotonNetwork.InRoom) 
            { 
                _expectedPlayerCount = PhotonNetwork.CurrentRoom.PlayerCount;
                _currentInstantiatedPlayers = 0;

                NetworkedObjectsManager.Instance.OnPlayerInstantiation += OnPlayerInstantiated;
            }
            else
            {
                _currentInstantiatedPlayers = _expectedPlayerCount;
                OnPlayerInstantiated(null);
            }
        }

        void LateUpdate()
        {
            if (!_initialized) return;

            Physics.SyncTransforms();
            UpdateCableData();
            ScheduleCableUpdateJob();
            UpdateCableVertices();
        }

        void OnDestroy()
        {
            if (NetworkedObjectsManager.Instance != null)
                NetworkedObjectsManager.Instance.OnPlayerInstantiation -= OnPlayerInstantiated;

            _currentInstantiatedPlayers = 0;
            _expectedPlayerCount = 0;
            _initialized = false;

            if (_nativeVertices.IsCreated) _nativeVertices.Dispose();
            if (_cableDataArray.IsCreated) _cableDataArray.Dispose();
            if (_reusableCableVertices.IsCreated) _reusableCableVertices.Dispose();
        }

        #endregion

        #region Public

        /// <summary>
        /// Enables or disables all cables managed by this system, as well as the system GameObject itself.
        /// </summary>
        /// <param name="enable">If true, cables are enabled; otherwise, they are disabled.</param>
        public void EnableAll(bool enable)
        {
            foreach (var cable in _meshCables)
                cable.gameObject.SetActive(enable);
            gameObject.SetActive(enable);
        }

        /// <summary>
        /// Registers a <see cref="MeshCable"/> with the calculator if it has not already been added.
        /// </summary>
        /// <param name="cable">The <see cref="MeshCable"/> to assign.</param>
        public void AssignCable(MeshCable cable)
        {
            if (!_meshCables.Contains(cable))
                _meshCables.Add(cable);
        }

        #endregion

        #region Private

        /// <summary>
        /// Initializes cables data.
        /// </summary>
        void InitializeCablesData()
        {
            int totalVertexCount = CalculateTotalVertexCount();
            if (totalVertexCount == 0) return;

            _nativeVertices = new NativeArray<float3>(totalVertexCount, Allocator.Persistent);
            _cableDataArray = new NativeArray<CableData>(_meshCables.Count, Allocator.Persistent);

            int maxVertexCount = CalculateMaxVertexCount();
            _reusableCableVertices = new NativeArray<float3>(maxVertexCount, Allocator.Persistent);

            InitializeCableData();
            InitializeCables();

            _initialized = true;
        }

        /// <summary>
        /// Initializes each cable in the list by calculating its vertices.
        /// </summary>
        void InitializeCables()
        {
            foreach (var cable in _meshCables)
            {
                cable.InitializeCableMesh();
            }
        }

        /// <summary>
        /// Prepares the cable data for each cable and stores it in the _cableDataArray.
        /// </summary>
        void InitializeCableData()
        {
            int vertexOffset = 0;
            for (int i = 0; i < _meshCables.Count; i++)
            {
                var cable = _meshCables[i];
                float3 pointA = cable.GetInverseTransformPoint(cable.PointA.position);
                float3 pointB = cable.GetInverseTransformPoint(cable.PointB.position);
                float3 startDirection = (cable.PointA.forward * cable.CableSlack);
                float3 endDirection = (cable.PointB.forward * cable.CableSlack);
                quaternion lookRotation = GetLookRotation(pointA, pointB);

                _cableDataArray[i] = new CableData
                {
                    Segments = cable.Segments,
                    RadialSegments = cable.RadialSegments,
                    CableRadius = cable.CableRadius,
                    PointA = pointA,
                    PointB = pointB,
                    StartControlPoint = pointA + startDirection,
                    EndControlPoint = pointB + endDirection,
                    LookRotation = lookRotation,
                    VertexOffset = vertexOffset
                };

                vertexOffset += (cable.Segments + 2) * (cable.RadialSegments + 1);
            }
        }

        /// <summary>
        /// Calculates the total number of vertices needed for all cables.
        /// </summary>
        /// <returns>Total vertex count.</returns>
        int CalculateTotalVertexCount()
        {
            int totalVertexCount = 0;
            foreach (var cable in _meshCables)
            {
                totalVertexCount += (cable.Segments + 2) * (cable.RadialSegments + 1);
            }
            return totalVertexCount;
        }

        /// <summary>
        /// Finds the maximum vertex count needed for a single cable.
        /// </summary>
        /// <returns>Maximum vertex count.</returns>
        int CalculateMaxVertexCount()
        {
            int maxVertexCount = 0;
            foreach (var cable in _meshCables)
            {
                maxVertexCount = math.max(maxVertexCount, (cable.Segments + 2) * (cable.RadialSegments + 1));
            }
            return maxVertexCount;
        }

        /// <summary>
        /// Updates the cable data with the latest positions.
        /// </summary>
        void UpdateCableData()
        {
            for (int i = 0; i < _meshCables.Count; i++)
            {
                var cable = _meshCables[i];
                CableData cableData = _cableDataArray[i];
                float3 pointA = cable.GetInverseTransformPoint(cable.PointA.position);
                float3 pointB = cable.GetInverseTransformPoint(cable.PointB.position);
                float3 startDirection = (cable.PointA.forward * cable.CableSlack);
                float3 endDirection = (cable.PointB.forward * cable.CableSlack);
                quaternion lookRotation = GetLookRotation(pointA, pointB);

                cableData.PointA = pointA;
                cableData.PointB = pointB;
                cableData.StartControlPoint = pointA + startDirection;
                cableData.EndControlPoint = pointB + endDirection;
                cableData.LookRotation = lookRotation;

                _cableDataArray[i] = cableData;
            }
        }

        /// <summary>
        /// Schedules the job to update cable vertices in parallel.
        /// </summary>
        void ScheduleCableUpdateJob()
        {
            var job = new UpdateCableJob
            {
                CableDataArray = _cableDataArray,
                Vertices = _nativeVertices
            };
            JobHandle jobHandle = job.Schedule(_nativeVertices.Length, 32);
            jobHandle.Complete();
        }

        /// <summary>
        /// Updates the mesh vertices for each cable.
        /// </summary>
        void UpdateCableVertices()
        {
            for (int i = 0; i < _meshCables.Count; i++)
            {
                var cable = _meshCables[i];
                CableData cableData = _cableDataArray[i];
                int vertexCount = (cableData.Segments + 2) * (cableData.RadialSegments + 1);

                NativeArray<float3>.Copy(_nativeVertices, cableData.VertexOffset, _reusableCableVertices, 0, vertexCount);
                cable.UpdateCableVertices(_reusableCableVertices.GetSubArray(0, vertexCount));
            }
        }

        /// <summary>
        /// Event handler for when a player object is instantiated.
        /// </summary>
        /// <param name="playerObject">The instantiated player GameObject.</param>
        void OnPlayerInstantiated(GameObject playerObject)
        {
            _currentInstantiatedPlayers++;
            
            if (_currentInstantiatedPlayers >= _expectedPlayerCount)
            {
                InitializeCablesData();
                NetworkedObjectsManager.Instance.OnPlayerInstantiation -= OnPlayerInstantiated;
            }        
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Returns the look rotation between two points, or identity if the distance is too small.
        /// </summary>
        quaternion GetLookRotation(float3 pointA, float3 pointB)
        {
            quaternion lookRotation = quaternion.LookRotationSafe(pointB - pointA, math.up());
            return math.length(pointB - pointA) < 0.001f ? quaternion.identity : lookRotation;
        }

        #endregion

        #region Job System

        [BurstCompile]
        struct UpdateCableJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<CableData> CableDataArray;
            public NativeArray<float3> Vertices;

            public void Execute(int index)
            {
                int cableIndex = FindCableIndex(index);
                var cableData = CableDataArray[cableIndex];
                int vertexOffset = cableData.VertexOffset;
                int i = (index - vertexOffset) / (cableData.RadialSegments + 1);
                int j = (index - vertexOffset) % (cableData.RadialSegments + 1);

                float t = (float)i / (cableData.Segments + 1);
                float3 cablePoint = CalculateCubicBezierPoint(t, cableData.PointA, cableData.StartControlPoint, cableData.EndControlPoint, cableData.PointB);
                float angle = (float)j / cableData.RadialSegments * math.PI * 2;
                float3 offset = new float3(math.cos(angle), math.sin(angle), 0) * cableData.CableRadius;
                Vertices[index] = cablePoint + math.mul(cableData.LookRotation, offset);

                if (i == 0) Vertices[index] = cableData.PointA;
                if (i == cableData.Segments + 1) Vertices[index] = cableData.PointB;
            }

            int FindCableIndex(int vertexIndex)
            {
                for (int i = 0; i < CableDataArray.Length - 1; i++)
                {
                    if (vertexIndex >= CableDataArray[i].VertexOffset && vertexIndex < CableDataArray[i + 1].VertexOffset)
                        return i;
                }
                return CableDataArray.Length - 1;
            }

            float3 CalculateCubicBezierPoint(float t, float3 p0, float3 p1, float3 p2, float3 p3)
            {
                float u = 1 - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;

                float3 point = uuu * p0;
                point += 3 * uu * t * p1;
                point += 3 * u * tt * p2;
                point += ttt * p3;

                return point;
            }
        }

        #endregion
    }
}
