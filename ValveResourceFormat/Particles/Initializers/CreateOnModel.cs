using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles on the model a control point is attached to, on the surface of its meshes or within its
    /// hitboxes. Particles stay at the control point when it follows no model.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateOnModel">C_INIT_CreateOnModel</seealso>
    class CreateOnModel : ParticleFunctionInitializer
    {
        private readonly int controlPointNumber;
        private readonly bool useMesh;
        private readonly string hitboxSetName = "default";

        public CreateOnModel(ParticleDefinitionParser parse) : base(parse)
        {
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);

            // Newer definitions name the model's control point in a model input instead
            if (parse.Data.GetSubCollection("m_modelInput") is { } modelInput && modelInput.ContainsKey("m_nControlPoint"))
            {
                controlPointNumber = parse.Nested(modelInput).Int32("m_nControlPoint", controlPointNumber);
            }

            useMesh = parse.Boolean("m_bUseMesh", useMesh);

            if (parse.Data.GetStringProperty("m_HitboxSetName") is { Length: > 0 } setName)
            {
                hitboxSetName = setName;
            }
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);
            var random = particleSystemState.Random;

            if (controlPoint.Model is { } model
                && model.TryGetPoint(useMesh, hitboxSetName, new Vector4(random.Next(), random.Next(), random.Next(), random.Next()), out var position))
            {
                particle.Position = position;
            }
            else
            {
                particle.Position = controlPoint.Position;
            }

            return particle;
        }
    }
}
