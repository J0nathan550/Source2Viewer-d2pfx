using System.IO;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.WeaponSkins
{
    /// <summary>
    /// How a paint kit is painted on, as its paint material's F_PAINT_STYLE combo numbers them.
    /// </summary>
    enum PaintStyle
    {
        SolidColor = 0,
        Hydrographic = 1,
        SprayPaint = 2,
        Anodized = 3,
        AnodizedMulticolored = 4,
        AnodizedAirbrushed = 5,
        CustomPaintJob = 6,
        Patina = 7,
        Gunsmith = 8,
    }

    /// <summary>
    /// The inputs a paint kit paints a weapon with: its paint material's parameters, with the variables its composite
    /// material and the composite materials that one includes set over them.
    /// </summary>
    sealed class PaintKitMaterial
    {
        private const string PaintContainer = "paint";

        public string PaintMaterial { get; }
        public PaintStyle Style { get; }

        private readonly Dictionary<string, long> ints = new(StringComparer.Ordinal);
        private readonly Dictionary<string, float> floats = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Vector4> vectors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> textures = new(StringComparer.Ordinal);

        private PaintKitMaterial(string paintMaterial, Material material, Dictionary<string, KVObject> variables, int fallbackStyle)
        {
            PaintMaterial = paintMaterial;

            foreach (var (key, value) in material.IntParams)
            {
                ints[key] = value;
            }

            foreach (var (key, value) in material.FloatParams)
            {
                floats[key] = value;
            }

            foreach (var (key, value) in material.VectorParams)
            {
                vectors[key] = value;
            }

            foreach (var (key, value) in material.TextureParams)
            {
                textures[key] = value;
            }

            foreach (var (name, variable) in variables)
            {
                SetVariable(name, variable);
            }

            Style = (PaintStyle)(ints.TryGetValue("F_PAINT_STYLE", out var style) ? style : fallbackStyle);
        }

        public float GetFloat(string name, float defaultValue = 0f) => floats.TryGetValue(name, out var value) ? value : defaultValue;

        public Vector4 GetVector(string name, Vector4 defaultValue = default) => vectors.TryGetValue(name, out var value) ? value : defaultValue;

        public bool GetBool(string name, bool defaultValue = false) => ints.TryGetValue(name, out var value) ? value != 0 : defaultValue;

        public string? GetTexture(string name) => textures.GetValueOrDefault(name);

        /// <summary>
        /// Reads a paint kit's composite material and the paint material it names.
        /// </summary>
        /// <param name="fallbackStyle">The style to paint with when the paint material does not say, from items_game.txt.</param>
        public static PaintKitMaterial Load(IFileLoader fileLoader, string compositeMaterial, int fallbackStyle)
        {
            string? paintMaterial = null;
            var variables = new Dictionary<string, KVObject>(StringComparer.Ordinal);

            ReadCompositeMaterial(fileLoader, compositeMaterial, variables, ref paintMaterial, depth: 0);

            if (paintMaterial == null)
            {
                throw new InvalidDataException($"\"{compositeMaterial}\" does not name a paint material");
            }

            using var resource = fileLoader.LoadFileCompiled(paintMaterial) ?? throw new FileNotFoundException($"\"{paintMaterial}\" was not found");

            if (resource.DataBlock is not Material material)
            {
                throw new InvalidDataException($"\"{paintMaterial}\" is not a material");
            }

            return new PaintKitMaterial(paintMaterial, material, variables, fallbackStyle);
        }

        /// <summary>
        /// Collects the paint material and loose variables of a composite material, the ones of the composite materials
        /// it includes first so its own win.
        /// </summary>
        private static void ReadCompositeMaterial(IFileLoader fileLoader, string path, Dictionary<string, KVObject> variables, ref string? paintMaterial, int depth)
        {
            if (depth > 8)
            {
                throw new InvalidDataException($"\"{path}\" includes too many composite materials");
            }

            using var resource = fileLoader.LoadFileCompiled(path) ?? throw new FileNotFoundException($"\"{path}\" was not found");

            if (resource.DataBlock is not BinaryKV3 data)
            {
                throw new InvalidDataException($"\"{path}\" is not a composite material");
            }

            foreach (var point in data.Data.Root.GetArray("m_Points") ?? [])
            {
                foreach (var procedure in point.GetArray("m_vecCompositeMaterialAssemblyProcedures") ?? [])
                {
                    foreach (var include in procedure.GetArray<string>("m_vecCompMatIncludes") ?? [])
                    {
                        ReadCompositeMaterial(fileLoader, include, variables, ref paintMaterial, depth + 1);
                    }

                    foreach (var container in procedure.GetArray("m_vecCompositeInputContainers") ?? [])
                    {
                        var sourceType = container.GetStringProperty("m_nCompositeMaterialInputContainerSourceType");

                        if (sourceType == "CONTAINER_SOURCE_TYPE_SPECIFIC_MATERIAL" && container.GetStringProperty("m_strAlias") == PaintContainer)
                        {
                            paintMaterial = container.GetStringProperty("m_strSpecificContainerMaterial") ?? paintMaterial;
                        }
                        else if (sourceType == "CONTAINER_SOURCE_TYPE_LOOSE_VARIABLES")
                        {
                            foreach (var variable in container.GetArray("m_vecLooseVariables") ?? [])
                            {
                                if (variable.GetStringProperty("m_strName") is { } name)
                                {
                                    variables[name] = variable;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void SetVariable(string name, KVObject variable)
        {
            switch (variable.GetStringProperty("m_nVariableType"))
            {
                case "LOOSE_VARIABLE_TYPE_BOOLEAN":
                    ints[name] = variable.GetBooleanProperty("m_bValueBoolean") ? 1 : 0;
                    break;

                case "LOOSE_VARIABLE_TYPE_INTEGER1":
                    ints[name] = variable.GetIntegerProperty("m_nValueIntX");
                    break;

                case "LOOSE_VARIABLE_TYPE_FLOAT1":
                    floats[name] = variable.GetFloatProperty("m_flValueFloatX");
                    break;

                case "LOOSE_VARIABLE_TYPE_FLOAT2":
                    vectors[name] = new Vector4(variable.GetFloatProperty("m_flValueFloatX"), variable.GetFloatProperty("m_flValueFloatY"), 0f, 0f);
                    break;

                case "LOOSE_VARIABLE_TYPE_COLOR4":
                    var color = variable.GetIntegerArray("m_cValueColor4");

                    if (color.Length >= 3)
                    {
                        vectors[name] = new Vector4(color[0] / 255f, color[1] / 255f, color[2] / 255f, color.Length > 3 ? color[3] / 255f : 1f);
                    }

                    break;

                case "LOOSE_VARIABLE_TYPE_RESOURCE_TEXTURE":
                    if (variable.GetStringProperty("m_strTextureRuntimeResourcePath") is { Length: > 0 } texture)
                    {
                        textures[name] = texture;
                    }

                    break;
            }
        }
    }
}
