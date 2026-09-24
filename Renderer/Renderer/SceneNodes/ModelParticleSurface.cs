using System.Linq;
using System.Threading;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// The meshes and hitboxes of a model scene node as it is posed, for effects to create their particles on. The
    /// meshes are read on the first point asked for, so models nothing creates particles on cost nothing.
    /// </summary>
    internal sealed class ModelParticleSurface : IParticleModel
    {
        private readonly ModelSceneNode node;
        private readonly Model model;
        private readonly Lazy<Dictionary<int, MeshSurface>> meshes;

        public ModelParticleSurface(ModelSceneNode node, Model model, IFileLoader fileLoader)
        {
            this.node = node;
            this.model = model;
            meshes = new(() => ReadMeshes(model, fileLoader), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc/>
        public bool TryGetPoint(bool useMesh, string hitboxSetName, Vector4 random, out Vector3 position)
        {
            // Either kind stands in for the other, a point anywhere on the model is closer than none
            return useMesh
                ? TryGetMeshPoint(random, out position) || TryGetHitboxPoint(hitboxSetName, random, out position)
                : TryGetHitboxPoint(hitboxSetName, random, out position) || TryGetMeshPoint(random, out position);
        }

        /// <summary>
        /// Picks a point on the meshes the model draws now, which leaves out other LoDs and hidden body group
        /// choices, with every area of the surface equally likely.
        /// </summary>
        private bool TryGetMeshPoint(Vector4 random, out Vector3 position)
        {
            position = default;

            var surfaces = meshes.Value;
            var shown = node.RenderableMeshes;
            var totalArea = 0f;

            foreach (var mesh in shown)
            {
                if (surfaces.TryGetValue(mesh.MeshIndex, out var surface))
                {
                    totalArea += surface.Area;
                }
            }

            if (totalArea <= 0f)
            {
                return false;
            }

            var pick = random.X * totalArea;
            MeshSurface? picked = null;

            foreach (var mesh in shown)
            {
                if (!surfaces.TryGetValue(mesh.MeshIndex, out var surface) || surface.Area <= 0f)
                {
                    continue;
                }

                picked = surface;

                if (pick < surface.Area)
                {
                    break;
                }

                pick -= surface.Area;
            }

            var triangle = picked!.FindTriangle(pick);
            var a = picked.Indices[triangle * 3];
            var b = picked.Indices[(triangle * 3) + 1];
            var c = picked.Indices[(triangle * 3) + 2];

            var u = random.Y;
            var v = random.Z;

            // Folds the unit square onto the triangle
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }

            var local = (SkinVertex(picked, a) * (1f - u - v)) + (SkinVertex(picked, b) * u) + (SkinVertex(picked, c) * v);
            position = Vector3.Transform(local, node.Transform);

            return true;
        }

        private Vector3 SkinVertex(MeshSurface surface, int vertex)
        {
            var bindPosition = surface.Positions[vertex];

            if (surface.BonesPerVertex == 0)
            {
                return bindPosition;
            }

            var controller = node.AnimationController;
            var pose = controller.Pose;
            var inverseBindPose = controller.InverseBindPose;

            var skinned = Vector3.Zero;
            var totalWeight = 0f;

            for (var i = 0; i < surface.BonesPerVertex; i++)
            {
                var weight = surface.Weights[(vertex * surface.BonesPerVertex) + i];
                var bone = surface.Bones[(vertex * surface.BonesPerVertex) + i];

                if (weight <= 0f || bone >= pose.Length)
                {
                    continue;
                }

                skinned += Vector3.Transform(bindPosition, inverseBindPose[bone] * pose[bone]) * weight;
                totalWeight += weight;
            }

            return totalWeight > 0f ? skinned / totalWeight : bindPosition;
        }

        /// <summary>
        /// Picks a point within one of the hitboxes of the set, or of the model's only set when it has no such set.
        /// </summary>
        private bool TryGetHitboxPoint(string hitboxSetName, Vector4 random, out Vector3 position)
        {
            position = default;

            var sets = model.HitboxSets;

            if (sets == null || sets.Count == 0)
            {
                return false;
            }

            var hitboxes = sets.GetValueOrDefault(hitboxSetName) ?? (sets.Count == 1 ? sets.Values.First() : null);

            if (hitboxes == null || hitboxes.Length == 0)
            {
                return false;
            }

            var scaled = random.X * hitboxes.Length;
            var index = Math.Min((int)scaled, hitboxes.Length - 1);

            // What is left of the draw once the hitbox is picked is still uniform, and places the point within a sphere
            var fraction = Math.Clamp(scaled - index, 0f, 1f);
            var hitbox = hitboxes[index];

            Vector3 local;

            if (hitbox.ShapeType == Hitbox.HitboxShape.Box)
            {
                local = new Vector3(
                    float.Lerp(hitbox.MinBounds.X, hitbox.MaxBounds.X, random.Y),
                    float.Lerp(hitbox.MinBounds.Y, hitbox.MaxBounds.Y, random.Z),
                    float.Lerp(hitbox.MinBounds.Z, hitbox.MaxBounds.Z, random.W));
            }
            else
            {
                // Spheres and capsules: a point along the capsule's axis, pushed out within its radius
                var cosTheta = (2f * random.Z) - 1f;
                var sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - (cosTheta * cosTheta)));
                var phi = 2f * MathF.PI * random.W;
                var direction = new Vector3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);

                local = Vector3.Lerp(hitbox.MinBounds, hitbox.MaxBounds, random.Y) + (direction * hitbox.ShapeRadius * MathF.Cbrt(fraction));
            }

            var controller = node.AnimationController;
            var bone = controller.Skeleton.GetBoneIndex(hitbox.BoneName);
            var boneTransform = bone >= 0 && bone < controller.Pose.Length ? controller.Pose[bone] : Matrix4x4.Identity;

            position = Vector3.Transform(Vector3.Transform(local, boneTransform), node.Transform);

            return true;
        }

        private static Dictionary<int, MeshSurface> ReadMeshes(Model model, IFileLoader fileLoader)
        {
            var surfaces = new Dictionary<int, MeshSurface>();

            void Add(Mesh mesh, int meshIndex)
            {
                if (ReadMesh(mesh, model.GetRemapTable(meshIndex)) is { } surface)
                {
                    surfaces[meshIndex] = surface;
                }
            }

            foreach (var embedded in model.GetEmbeddedMeshes())
            {
                Add(embedded.Mesh, embedded.MeshIndex);
            }

            foreach (var reference in model.GetReferenceMeshNamesAndLoD())
            {
                if (fileLoader.LoadFileCompiled(reference.MeshName)?.DataBlock is Mesh mesh)
                {
                    Add(mesh, reference.MeshIndex);
                }
            }

            return surfaces;
        }

        private static MeshSurface? ReadMesh(Mesh mesh, int[]? remapTable)
        {
            var vbib = mesh.VBIB;
            var positions = new List<Vector3>();
            var bones = new List<ushort>();
            var weights = new List<float>();
            var indices = new List<int>();
            var bufferStarts = new Dictionary<int, int>();
            var bonesPerVertex = -1;

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    if (drawCall.GetEnumValue<RenderPrimitiveType>("m_nPrimitiveType") != RenderPrimitiveType.RENDER_PRIM_TRIANGLES)
                    {
                        continue;
                    }

                    var vertexBufferIndex = drawCall.GetArray("m_vertexBuffers")[0].GetInt32Property("m_hBuffer");

                    if (!bufferStarts.TryGetValue(vertexBufferIndex, out var bufferStart))
                    {
                        bufferStart = ReadVertexBuffer(vbib.VertexBuffers[vertexBufferIndex], remapTable, positions, bones, weights, ref bonesPerVertex);
                        bufferStarts.Add(vertexBufferIndex, bufferStart);
                    }

                    if (bufferStart < 0)
                    {
                        continue;
                    }

                    var indexBuffer = vbib.IndexBuffers[drawCall.GetSubCollection("m_indexBuffer").GetInt32Property("m_hBuffer")];
                    var drawIndices = GltfModelExporter.ReadIndices(indexBuffer, drawCall.GetInt32Property("m_nStartIndex"),
                        drawCall.GetInt32Property("m_nIndexCount"), drawCall.GetInt32Property("m_nBaseVertex"));

                    foreach (var index in drawIndices)
                    {
                        indices.Add(bufferStart + index);
                    }
                }
            }

            if (indices.Count < 3)
            {
                return null;
            }

            var positionArray = positions.ToArray();
            var triangleCount = indices.Count / 3;
            var cumulativeAreas = new float[triangleCount];
            var total = 0f;

            for (var i = 0; i < triangleCount; i++)
            {
                var a = positionArray[indices[i * 3]];
                var b = positionArray[indices[(i * 3) + 1]];
                var c = positionArray[indices[(i * 3) + 2]];

                total += Vector3.Cross(b - a, c - a).Length() * 0.5f;
                cumulativeAreas[i] = total;
            }

            return new MeshSurface
            {
                Positions = positionArray,
                Bones = [.. bones],
                Weights = [.. weights],
                BonesPerVertex = Math.Max(bonesPerVertex, 0),
                Indices = [.. indices],
                CumulativeAreas = cumulativeAreas,
            };
        }

        /// <summary>
        /// Appends a vertex buffer's positions and bone influences, returning where its vertices start, or -1 when it
        /// has no positions.
        /// </summary>
        private static int ReadVertexBuffer(VBIB.OnDiskBufferData vertexBuffer, int[]? remapTable,
            List<Vector3> positions, List<ushort> bones, List<float> weights, ref int bonesPerVertex)
        {
            var fields = vertexBuffer.InputLayoutFields;
            var positionField = fields.FirstOrDefault(static field => field.SemanticName == "POSITION");

            if (positionField.SemanticName != "POSITION" || vertexBuffer.ElementCount == 0)
            {
                return -1;
            }

            var start = positions.Count;
            var count = (int)vertexBuffer.ElementCount;
            positions.AddRange(VBIB.GetVector3AttributeArray(vertexBuffer, positionField));

            var indicesField = fields.FirstOrDefault(static field => field.SemanticName == "BLENDINDICES" && field.SemanticIndex == 0);
            var weightsField = fields.FirstOrDefault(static field => field.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS" && field.SemanticIndex == 0);

            var vertexBones = indicesField.SemanticName == "BLENDINDICES" ? VBIB.GetBlendIndicesArray(vertexBuffer, indicesField, remapTable) : [];
            var perVertex = vertexBones.Length / count;

            // Every buffer of a mesh is expected to carry the same number of influences, the rest are left rigid
            if (bonesPerVertex < 0)
            {
                bonesPerVertex = perVertex;
            }

            if (bonesPerVertex == 0)
            {
                return start;
            }

            var vertexWeights = weightsField.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS"
                ? VBIB.GetBlendWeightsArray(vertexBuffer, weightsField)
                : [];
            var weightsPerVertex = vertexWeights.Length * 4 / count;

            for (var vertex = 0; vertex < count; vertex++)
            {
                for (var i = 0; i < bonesPerVertex; i++)
                {
                    bones.Add(i < perVertex ? vertexBones[(vertex * perVertex) + i] : (ushort)0);

                    // Without weights, a vertex follows its first bone
                    var weight = weightsPerVertex == 0
                        ? (i == 0 ? 1f : 0f)
                        : i < weightsPerVertex ? GetComponent(vertexWeights[((vertex * weightsPerVertex) + i) / 4], i % 4) : 0f;

                    weights.Add(i < perVertex ? weight : 0f);
                }
            }

            return start;
        }

        private static float GetComponent(Vector4 vector, int component) => component switch
        {
            0 => vector.X,
            1 => vector.Y,
            2 => vector.Z,
            _ => vector.W,
        };

        private sealed class MeshSurface
        {
            /// <summary>Bind pose positions, in model space.</summary>
            public required Vector3[] Positions { get; init; }

            /// <summary><see cref="BonesPerVertex"/> model bone indices per vertex.</summary>
            public required ushort[] Bones { get; init; }

            /// <summary>The weights of <see cref="Bones"/>, which need not add up to one.</summary>
            public required float[] Weights { get; init; }

            /// <summary>How many bones each vertex follows, 0 for a rigid mesh.</summary>
            public required int BonesPerVertex { get; init; }

            /// <summary>Three vertex indices per triangle.</summary>
            public required int[] Indices { get; init; }

            /// <summary>The area of every triangle up to and including each one, in bind pose.</summary>
            public required float[] CumulativeAreas { get; init; }

            public float Area => CumulativeAreas[^1];

            /// <summary>The triangle an area picked in [0, <see cref="Area"/>) falls in.</summary>
            public int FindTriangle(float area)
            {
                var index = Array.BinarySearch(CumulativeAreas, area);

                if (index < 0)
                {
                    index = ~index;
                }

                return Math.Min(index, CumulativeAreas.Length - 1);
            }
        }
    }
}
