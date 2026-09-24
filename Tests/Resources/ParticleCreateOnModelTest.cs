using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Initializers;

namespace Tests.Resources
{
    public class ParticleCreateOnModelTest
    {
        private sealed class FixedPointModel(Vector3 point) : IParticleModel
        {
            public bool? UsedMesh { get; private set; }
            public string? UsedHitboxSet { get; private set; }

            public bool TryGetPoint(bool useMesh, string hitboxSetName, Vector4 random, out Vector3 position)
            {
                UsedMesh = useMesh;
                UsedHitboxSet = hitboxSetName;
                position = point;
                return true;
            }
        }

        private sealed class EmptyModel : IParticleModel
        {
            public bool TryGetPoint(bool useMesh, string hitboxSetName, Vector4 random, out Vector3 position)
            {
                position = default;
                return false;
            }
        }

        private static CreateOnModel Create(string definition)
            => new(new ParticleDefinitionParser(TestFixtures.ParseKV3($"<!-- kv3 encoding:text:version{{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d}} format:generic:version{{7412167c-06e9-4698-aff2-e63eb59037e7}} -->\n{definition}"), NullLogger.Instance));

        private static Vector3 Spawn(CreateOnModel initializer, ParticleSystemState state)
        {
            var particle = new Particle();
            initializer.Initialize(ref particle, new ParticleCollection(particle, 1), state);
            return particle.Position;
        }

        [Test]
        public async Task PlacesParticlesOnTheControlPointModel()
        {
            var model = new FixedPointModel(new Vector3(1, 2, 3));
            var state = new ParticleSystemState();
            state.GetControlPoint(0).Model = model;

            var position = Spawn(Create("{ _class = \"C_INIT_CreateOnModel\" m_bUseMesh = true }"), state);

            using (Assert.Multiple())
            {
                await Assert.That(position).IsEqualTo(new Vector3(1, 2, 3));
                await Assert.That(model.UsedMesh).IsTrue();
                await Assert.That(model.UsedHitboxSet).IsEqualTo("default");
            }
        }

        [Test]
        public async Task ReadsTheModelControlPointAndHitboxSet()
        {
            var model = new FixedPointModel(new Vector3(4, 5, 6));
            var state = new ParticleSystemState();
            state.GetControlPoint(2).Model = model;

            var position = Spawn(Create("{ _class = \"C_INIT_CreateOnModel\" m_modelInput = { m_nControlPoint = 2 } m_HitboxSetName = \"effects\" }"), state);

            using (Assert.Multiple())
            {
                await Assert.That(position).IsEqualTo(new Vector3(4, 5, 6));
                await Assert.That(model.UsedMesh).IsFalse();
                await Assert.That(model.UsedHitboxSet).IsEqualTo("effects");
            }
        }

        [Test]
        public async Task StaysAtTheControlPointWithoutAModel()
        {
            var state = new ParticleSystemState();
            state.GetControlPoint(0).Position = new Vector3(7, 8, 9);
            state.GetControlPoint(1).Position = new Vector3(-1, -2, -3);
            state.GetControlPoint(1).Model = new EmptyModel();

            using (Assert.Multiple())
            {
                await Assert.That(Spawn(Create("{ _class = \"C_INIT_CreateOnModel\" }"), state)).IsEqualTo(new Vector3(7, 8, 9));
                await Assert.That(Spawn(Create("{ _class = \"C_INIT_CreateOnModel\" m_nControlPointNumber = 1 }"), state)).IsEqualTo(new Vector3(-1, -2, -3));
            }
        }
    }
}
