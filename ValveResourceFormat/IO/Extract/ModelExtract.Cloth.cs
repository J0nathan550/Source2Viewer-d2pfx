using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for cloth simulated on bones: the chains of joints the compiled cloth model was built
/// from, with the springs, goal and gravity of each joint, and the nodes that align bones between cloth nodes.
/// </summary>
partial class ModelExtract
{
    // The compiled gravity of a joint is its authored gravity scale times this
    private const float ClothGravity = 360f;

    // Nodes the compiler adds around a joint to give its chain a width are named after it with this prefix
    private const string ClothExtrusionPrefix = "$cc";

    // Goal damping compiles into how much of the way to its animated position a node is pulled on top of its goal
    // strength, with no closed form. These are -ln((1 - vertex attraction) / (1 - force attraction)) for the authored
    // goal strengths (rows) and goal dampings (columns), which grow with the damping.
    private static readonly float[] GoalDampings = [0f, 0.005f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.06f, 0.08f, 0.1f, 0.15f, 0.2f, 0.3f, 0.5f, 0.75f, 1f];
    private static readonly float[] GoalStrengths = [0.05f, 0.1f, 0.15f, 0.2f, 0.25f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f];
    private static readonly float[,] GoalDampingResponses =
    {
        { 0f, 0.8671f, 1.6095f, 2.6901f, 3.4266f, 3.9738f, 4.4067f, 4.7639f, 5.3320f, 5.7746f, 6.5819f, 7.1561f, 7.9661f, 8.9871f, 9.7900f, 10.3814f },
        { 0f, 0.3151f, 0.6227f, 1.1934f, 1.6899f, 2.1145f, 2.4787f, 2.7944f, 3.3174f, 3.7380f, 4.5227f, 5.0885f, 5.8926f, 6.9108f, 7.7209f, 8.2971f },
        { 0f, 0.1722f, 0.3432f, 0.6767f, 0.9932f, 1.2884f, 1.5609f, 1.8112f, 2.2519f, 2.6261f, 3.3577f, 3.9030f, 4.6915f, 5.7014f, 6.5090f, 7.0823f },
        { 0f, 0.1122f, 0.2240f, 0.4453f, 0.6614f, 0.8703f, 1.0707f, 1.2617f, 1.6148f, 1.9308f, 2.5852f, 3.0963f, 3.8574f, 4.8520f, 5.6544f, 6.2268f },
        { 0f, 0.0806f, 0.1611f, 0.3211f, 0.4792f, 0.6344f, 0.7859f, 0.9333f, 1.2142f, 1.4752f, 2.0441f, 2.5113f, 3.2334f, 4.2047f, 4.9990f, 5.5687f },
        { 0f, 0.0617f, 0.1233f, 0.2462f, 0.3681f, 0.4887f, 0.6076f, 0.7244f, 0.9509f, 1.1666f, 1.6553f, 2.0750f, 2.7498f, 3.6899f, 4.4732f, 5.0386f },
        { 0f, 0.0409f, 0.0817f, 0.1632f, 0.2445f, 0.3254f, 0.4058f, 0.4855f, 0.6426f, 0.7960f, 1.1596f, 1.4920f, 2.0647f, 2.9253f, 3.6772f, 4.2306f },
        { 0f, 0.0302f, 0.0605f, 0.1209f, 0.1812f, 0.2413f, 0.3012f, 0.3609f, 0.4792f, 0.5959f, 0.8786f, 1.1458f, 1.6283f, 2.4027f, 3.1147f, 3.6519f },
        { 0f, 0.0243f, 0.0486f, 0.0972f, 0.1457f, 0.1941f, 0.2424f, 0.2906f, 0.3864f, 0.4813f, 0.7138f, 0.9373f, 1.3525f, 2.0511f, 2.7227f, 3.2419f },
        { 0f, 0.0211f, 0.0421f, 0.0842f, 0.1263f, 0.1683f, 0.2103f, 0.2521f, 0.3355f, 0.4183f, 0.6219f, 0.8195f, 1.1921f, 1.8371f, 2.4766f, 2.9806f },
        { 0f, 0.0200f, 0.0400f, 0.0800f, 0.1200f, 0.1599f, 0.1997f, 0.2395f, 0.3187f, 0.3975f, 0.5915f, 0.7803f, 1.1379f, 1.7632f, 2.3900f, 2.8878f },
        { 0f, 0.0225f, 0.0450f, 0.0900f, 0.1349f, 0.1797f, 0.2245f, 0.2692f, 0.3581f, 0.4463f, 0.6628f, 0.8720f, 1.2641f, 1.9340f, 2.5888f, 3.1002f },
    };

    /// <summary>
    /// The goal damping a joint was authored with, from how much more than its goal strength it is pulled to its
    /// animated position.
    /// </summary>
    private static float GetGoalDamping(float goalStrength, float forceAttraction, float vertexAttraction)
    {
        if (vertexAttraction <= forceAttraction || forceAttraction >= 1f || vertexAttraction >= 1f)
        {
            return 0f;
        }

        var response = -MathF.Log((1f - vertexAttraction) / (1f - forceAttraction));

        // The responses for this goal strength, between the two measured strengths around it
        var upper = Math.Clamp(Array.FindIndex(GoalStrengths, strength => strength >= goalStrength), 1, GoalStrengths.Length - 1);
        var lower = upper - 1;
        var blend = Math.Clamp((goalStrength - GoalStrengths[lower]) / (GoalStrengths[upper] - GoalStrengths[lower]), 0f, 1f);

        float ResponseAt(int damping) => float.Lerp(GoalDampingResponses[lower, damping], GoalDampingResponses[upper, damping], blend);

        for (var i = 1; i < GoalDampings.Length; i++)
        {
            var previous = ResponseAt(i - 1);
            var next = ResponseAt(i);

            if (response <= next)
            {
                var amount = next > previous ? (response - previous) / (next - previous) : 0f;
                return MathF.Round(float.Lerp(GoalDampings[i - 1], GoalDampings[i], amount), 4);
            }
        }

        return GoalDampings[^1];
    }

    /// <summary>
    /// Whether all of the model's cloth is simulated on its bones, which <see cref="AddClothNodes"/> rebuilds. Cloth
    /// simulated on a mesh's vertices is left to the full reconstruction.
    /// </summary>
    private bool HasOnlyBoneCloth()
        => GetClothModel() is { } cloth && Enumerable.Range(0, cloth.Names.Length).All(cloth.IsBoneNode);

    private ClothModel? GetClothModel()
    {
        if (physAggregateData?.Data is not { } physics
            || !physics.ContainsKey("m_pFeModel")
            || physics.GetSubCollection("m_pFeModel") is not { } feModel
            || feModel.GetArray<string>("m_CtrlName") is not { Length: > 0 } names)
        {
            return null;
        }

        var parents = feModel.GetIntegerArray("m_SkelParents");

        if (parents.Length != names.Length)
        {
            return null;
        }

        // Cloth simulated on a mesh adds nodes of its own for the mesh's vertices, only the bones are rebuilt
        var bones = model?.Skeleton.Bones.Select(GetExportBoneName).ToHashSet(StringComparer.Ordinal) ?? [];
        return new ClothModel(feModel, names, parents, bones);
    }

    private void AddClothNodes(ModelDocLists lists)
    {
        if (GetClothModel() is not { } cloth)
        {
            return;
        }

        var chains = BuildClothChains(cloth);
        var alignments = BuildClothNodeAlignments(cloth);

        if (chains.Count == 0)
        {
            return;
        }

        var (softbody, children) = MakeListNode("Softbody");

        foreach (var node in alignments.Concat(chains))
        {
            children.Add(node);
        }

        lists.RootChildren.Add(softbody);
    }

    /// <summary>
    /// One chain per tree of joints: the bones the cloth simulates, each listed after its parent.
    /// </summary>
    private static List<KVObject> BuildClothChains(ClothModel cloth)
    {
        var chains = new List<KVObject>();
        var joints = Enumerable.Range(0, cloth.Names.Length).Where(cloth.IsJoint).ToList();

        foreach (var root in joints.Where(joint => cloth.GetParentJoint(joint) < 0))
        {
            if (!cloth.IsBoneTree(root))
            {
                continue;
            }

            // Depth first, as joints given a width are aligned with the joint listed after them
            var ordered = new List<int>();
            var pending = new Stack<int>([root]);

            while (pending.TryPop(out var joint))
            {
                ordered.Add(joint);

                foreach (var child in joints.Where(child => cloth.GetParentJoint(child) == joint).Reverse())
                {
                    pending.Push(child);
                }
            }

            // Anchors left behind by cloth simulated on a mesh have nothing of their own to simulate
            if (!ordered.Any(cloth.IsSimulated))
            {
                continue;
            }

            var jointNodes = MakeArray(ordered.Select(cloth.BuildJoint));
            var chain = KVObject.Collection();
            chain.Add("joints", jointNodes);
            chain.Add("version", 2);

            chains.Add(MakeNode("ClothChain",
                ("root_bone", cloth.GetBoneName(root)),
                ("chain", chain)));
        }

        return chains;
    }

    /// <summary>
    /// Nodes that orient a cloth node by the nodes around it, which bones attached to it follow.
    /// </summary>
    private static List<KVObject> BuildClothNodeAlignments(ClothModel cloth)
    {
        var alignments = new List<KVObject>();

        foreach (var nodeBase in cloth.FeModel.GetArray("m_NodeBases") ?? [])
        {
            var node = nodeBase.GetInt32Property("nNode");
            int[] bases = [nodeBase.GetInt32Property("nNodeX0"), nodeBase.GetInt32Property("nNodeX1"),
                nodeBase.GetInt32Property("nNodeY0"), nodeBase.GetInt32Property("nNodeY1")];

            if (!cloth.IsBoneNode(node) || !bases.All(cloth.IsBoneNode))
            {
                continue;
            }

            alignments.Add(MakeNode("ClothNode",
                ("name", $"cloth_alignment_{alignments.Count}"),
                ("cloth_node_root_bone", cloth.GetBoneName(node)),
                ("is_static", node < cloth.StaticNodeCount),
                ("allow_rotation", node >= cloth.RotationLockedStaticNodeCount),
                ("transform_alignment", 4),
                ("node_base_x0", cloth.GetBoneName(bases[0])),
                ("node_base_x1", cloth.GetBoneName(bases[1])),
                ("node_base_y0", cloth.GetBoneName(bases[2])),
                ("node_base_y1", cloth.GetBoneName(bases[3]))));
        }

        return alignments;
    }

    private sealed class ClothModel
    {
        public KVObject FeModel { get; }
        public string[] Names { get; }
        public int StaticNodeCount { get; }
        public int RotationLockedStaticNodeCount { get; }

        private readonly long[] parents;
        private readonly HashSet<string> bones;
        private readonly KVObject[] integrators;
        private readonly float[] collisionRadii;
        private readonly float[] frictions;
        private readonly List<(int Node0, int Node1, float Relaxation)> rods = [];
        private readonly Dictionary<int, List<(int Node, Vector3 Offset)>> sideNodes = [];
        private readonly HashSet<int> addedNodes = [];
        private readonly Dictionary<int, string> vertexMaps = [];

        public ClothModel(KVObject feModel, string[] names, long[] parents, HashSet<string> bones)
        {
            FeModel = feModel;
            Names = names;
            this.parents = parents;
            this.bones = bones;
            StaticNodeCount = feModel.GetInt32Property("m_nStaticNodes");
            RotationLockedStaticNodeCount = feModel.GetInt32Property("m_nRotLockStaticNodes");
            integrators = [.. feModel.GetArray("m_NodeIntegrator") ?? []];
            collisionRadii = feModel.ContainsKey("m_NodeCollisionRadii") ? feModel.GetFloatArray("m_NodeCollisionRadii") : [];
            frictions = feModel.ContainsKey("m_DynNodeFriction") ? feModel.GetFloatArray("m_DynNodeFriction") : [];

            foreach (var rod in feModel.GetArray("m_Rods") ?? [])
            {
                var nodes = rod.GetIntegerArray("nNode");

                if (nodes.Length == 2)
                {
                    rods.Add(((int)nodes[0], (int)nodes[1], rod.GetFloatProperty("flRelaxationFactor", 1f)));
                }
            }

            foreach (var offset in feModel.GetArray("m_CtrlOffsets") ?? [])
            {
                var parent = offset.GetInt32Property("nCtrlParent");
                var child = offset.GetInt32Property("nCtrlChild");

                if (!IsNode(parent) || !IsNode(child) || !names[child].StartsWith(ClothExtrusionPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var vector = offset.GetFloatArray("vOffset");

                if (!sideNodes.TryGetValue(parent, out var sides))
                {
                    sideNodes[parent] = sides = [];
                }

                sides.Add((child, vector.Length >= 3 ? new Vector3(vector[0], vector[1], vector[2]) : Vector3.Zero));
                addedNodes.Add(child);
            }

            // Each map covers a run of nodes, e.g. the ones an animation event offsets from the ground
            foreach (var map in feModel.GetArray("m_VertexMaps") ?? [])
            {
                var name = map.GetStringProperty("sName");
                var first = map.GetInt32Property("nVertexBase");

                for (var node = first; node < first + map.GetInt32Property("nVertexCount"); node++)
                {
                    if (IsNode(node) && !string.IsNullOrEmpty(name))
                    {
                        vertexMaps[node] = name;
                    }
                }
            }
        }

        public bool IsNode(int node) => node >= 0 && node < Names.Length;

        /// <summary>Whether the node is a bone of the chain, rather than one the compiler added around a bone.</summary>
        public bool IsJoint(int node) => IsNode(node) && !addedNodes.Contains(node);

        public int GetParentJoint(int node) => IsJoint((int)parents[node]) ? (int)parents[node] : -1;

        /// <summary>Whether the node is a bone of the model, or one the compiler added around a bone.</summary>
        public bool IsBoneNode(int node)
            => IsNode(node) && (addedNodes.Contains(node) ? bones.Contains(GetBoneName(sideNodes.First(pair => pair.Value.Any(side => side.Node == node)).Key)) : bones.Contains(GetBoneName(node)));

        public bool IsSimulated(int joint) => joint >= StaticNodeCount || (sideNodes.TryGetValue(joint, out var sides) && sides.Any(side => side.Node >= StaticNodeCount));

        /// <summary>Whether every joint from this one down is a bone of the model.</summary>
        public bool IsBoneTree(int root)
        {
            var pending = new Stack<int>([root]);

            while (pending.TryPop(out var joint))
            {
                if (!IsBoneNode(joint))
                {
                    return false;
                }

                foreach (var child in Enumerable.Range(0, Names.Length).Where(child => IsJoint(child) && GetParentJoint(child) == joint))
                {
                    pending.Push(child);
                }
            }

            return true;
        }

        /// <summary>The node's name as the exported skeleton has it, see <see cref="GetExportBoneName"/>.</summary>
        public string GetBoneName(int node)
            => Names[node].StartsWith('$') && !addedNodes.Contains(node) ? $"_{Names[node][1..]}" : Names[node];

        public KVObject BuildJoint(int node)
        {
            var joint = KVObject.Collection();
            var parent = GetParentJoint(node);
            var grandParent = parent >= 0 ? GetParentJoint(parent) : -1;

            // A joint given a width follows the nodes added around it, which hold what is simulated
            var simulated = sideNodes.TryGetValue(node, out var sides) && sides.Count > 0 ? sides[0].Node : node;
            var dynamicIndex = simulated - StaticNodeCount;

            joint.Add("joint_name", GetBoneName(node));

            if (parent >= 0)
            {
                joint.Add("joint_parent", GetBoneName(parent));
            }

            joint.Add("simulate", node >= StaticNodeCount);
            joint.Add("allow_rotation", node >= RotationLockedStaticNodeCount);

            if (FindRod(parent, node) is { } stretch)
            {
                joint.Add("stretch_spring", stretch);
            }

            if (FindRod(grandParent, node) is { } bend)
            {
                joint.Add("bend_spring", bend);
            }

            var children = Enumerable.Range(0, Names.Length).Where(child => IsJoint(child) && GetParentJoint(child) == node).ToList();

            for (var i = 0; i < children.Count; i++)
            {
                if (children.Skip(i + 1).Select(other => FindRod(children[i], other)).FirstOrDefault(rod => rod != null) is { } sibling)
                {
                    joint.Add("child_sibling_spring", sibling);
                    break;
                }
            }

            if (dynamicIndex >= 0 && dynamicIndex < collisionRadii.Length)
            {
                joint.Add("collision_radius", collisionRadii[dynamicIndex]);
            }

            if (dynamicIndex >= 0 && dynamicIndex < frictions.Length)
            {
                joint.Add("friction", frictions[dynamicIndex]);
            }

            if (simulated < integrators.Length)
            {
                // Goal strength and drag compile as the cube of the authored value
                var integrator = integrators[simulated];
                var forceAttraction = Math.Clamp(integrator.GetFloatProperty("flAnimationForceAttraction"), 0f, 1f);
                var goalStrength = MathF.Cbrt(forceAttraction);

                joint.Add("goal_strength", goalStrength);
                joint.Add("goal_damping", GetGoalDamping(goalStrength, forceAttraction, integrator.GetFloatProperty("flAnimationVertexAttraction")));
                joint.Add("drag", MathF.Cbrt(Math.Clamp(integrator.GetFloatProperty("flPointDamping"), 0f, 1f)));
                joint.Add("gravity_z", integrator.GetFloatProperty("flGravity") / ClothGravity);
            }

            if (vertexMaps.TryGetValue(node, out var vertexMap))
            {
                joint.Add("vertex_map", vertexMap);
            }

            if (sides is { Count: > 0 })
            {
                // Sides spread along the bone's Y and Z, or along X and Y when the bone points along Z
                var side = sides[0].Offset;
                var alongZ = MathF.Abs(side.X) > MathF.Max(MathF.Abs(side.Y), MathF.Abs(side.Z));
                var twist = alongZ ? MathF.Atan2(-side.X, side.Y) : MathF.Atan2(side.Z, side.Y);

                joint.Add("extrude_sides", sides.Count);
                joint.Add("extrude_radius", sides.Average(static offset => offset.Offset.Length()));
                joint.Add("extrude_twist", float.RadiansToDegrees(twist));
                joint.Add("extrude_forward_axis", alongZ ? "Z" : "X");
            }

            return joint;
        }

        private float? FindRod(int node0, int node1)
        {
            if (node0 < 0 || node1 < 0)
            {
                return null;
            }

            foreach (var (a, b, relaxation) in rods)
            {
                if ((a == node0 && b == node1) || (a == node1 && b == node0))
                {
                    return relaxation;
                }
            }

            return null;
        }
    }
}
