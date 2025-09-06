using UnityEngine;
using Sirenix.OdinInspector;
using Unity.Collections;
using Unity.Mathematics;

namespace AEB.Systems.Cable
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MeshCable : MonoBehaviour
    {
        #region Fields

        /// <summary>
        /// The start point of the cable.
        /// </summary>
        [Required]
        public Transform PointA;

        /// <summary>
        /// The end point of the cable.
        /// </summary>
        [Required]
        public Transform PointB;

        /// <summary>
        /// Number of segments along the length of the cable.
        /// </summary>
        [Range(1, 100)]
        public int Segments = 10;

        /// <summary>
        /// Number of segments around the circumference of the cable.
        /// </summary>
        [Range(3, 36)]
        public int RadialSegments = 12;

        /// <summary>
        /// Amount of slack in the cable.
        /// </summary>
        [Range(0, 1)]
        public float CableSlack = 0.1f;

        /// <summary>
        /// Radius of the cable.
        /// </summary>
        [Range(0.001f, 1f)]
        public float CableRadius = 0.05f;

        Mesh _cableMesh;

        #endregion

        #region Unity

        void Awake()
        {
            Calculator.Instance.AssignCable(this);
        }

        #endregion

        #region Public

        /// <summary>
        /// Initializes the cable mesh with vertices, triangles, and UVs.
        /// </summary>
        public void InitializeCableMesh()
        {
            if (_cableMesh == null)
            {
                _cableMesh = new Mesh
                {
                    name = "CableMesh"
                };
                _cableMesh.MarkDynamic();
                GetComponent<MeshFilter>().mesh = _cableMesh;

                int vertexCount = (Segments + 2) * (RadialSegments + 1);
                int[] triangles = new int[(Segments + 1) * RadialSegments * 6];
                Vector2[] uvs = new Vector2[vertexCount];

                InitializeTrianglesAndUVs(triangles, uvs);

                _cableMesh.vertices = new Vector3[vertexCount];
                _cableMesh.triangles = triangles;
                _cableMesh.uv = uvs;
                _cableMesh.RecalculateNormals();
            }
        }

        /// <summary>
        /// Updates the cable vertices with the given vertex data.
        /// </summary>
        /// <param name="vertices">NativeArray containing the updated vertex positions.</param>
        public void UpdateCableVertices(NativeArray<float3> vertices)
        {
            _cableMesh.SetVertices(vertices);
            _cableMesh.RecalculateNormals();
            _cableMesh.RecalculateBounds();
        }

        /// <summary>
        /// Transforms a world position into the local space of the cable.
        /// </summary>
        /// <param name="position">World space position.</param>
        /// <returns>Position in local space of the cable.</returns>
        public Vector3 GetInverseTransformPoint(Vector3 position)
        {
            return transform.InverseTransformPoint(position);
        }


        #endregion

        #region Private

        void InitializeTrianglesAndUVs(int[] triangles, Vector2[] uvs)
        {
            int triangleIndex = 0;
            for (int i = 0; i <= Segments; i++)
            {
                for (int j = 0; j < RadialSegments; j++)
                {
                    int current = i * (RadialSegments + 1) + j;
                    int next = current + RadialSegments + 1;

                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;

                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            for (int i = 0; i <= Segments + 1; i++)
                for (int j = 0; j <= RadialSegments; j++)
                    uvs[i * (RadialSegments + 1) + j] = new Vector2((float)j / RadialSegments, (float)i / (Segments + 1));
        }

        #endregion
    }
}