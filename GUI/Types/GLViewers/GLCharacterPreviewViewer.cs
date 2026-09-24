using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// A model the character preview shows.
    /// </summary>
    /// <param name="Path">The model as named in scripts, e.g. "models/heroes/axe/axe.vmdl".</param>
    /// <param name="Skin">Index of the material group to show it with.</param>
    /// <param name="BodyGroups">The choice to show of body groups, by name, e.g. the arcana one the equipped arcana sets.</param>
    /// <param name="Particles">
    /// The effects to play on the model besides the ones the model creates itself, e.g. an item's ambient effect, or null
    /// to show the model without any.
    /// </param>
    sealed record PreviewModel(string Path, int Skin, IReadOnlyDictionary<string, int>? BodyGroups = null, IReadOnlyList<string>? Particles = null);

    /// <summary>
    /// Previews a hero playing its idle while wearing a set of item models, which follow the hero's skeleton, and the
    /// effects they play.
    /// </summary>
    class GLCharacterPreviewViewer : GLSingleNodeViewer
    {
        private readonly List<ModelSceneNode> modelNodes = [];
        private readonly List<ParticleSceneNode> particleNodes = [];
        private IReadOnlyList<PreviewModel> models = [];
        private int requestedVersion;

        public GLCharacterPreviewViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext)
        {
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            // The dialog hosting the preview has its own controls
            UiControl?.HideSidebar();
        }

        protected override void LoadScene()
        {
            AddModels(models);
        }

        /// <summary>
        /// Replaces the shown models, loading them on a worker thread. The first one is the hero, which the others follow.
        /// When called again before a load finishes, only the latest set is shown.
        /// </summary>
        /// <param name="previewModels">The models to show.</param>
        /// <param name="frameCamera">Whether to move the camera to fit the new models, rather than keeping the user's view.</param>
        public void SetModels(IReadOnlyList<PreviewModel> previewModels, bool frameCamera)
        {
            var version = Interlocked.Increment(ref requestedVersion);
            models = previewModels;

            // Before the viewer is loaded, the models are picked up by LoadScene
            if (GraphicsContext == null)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                if (version != Volatile.Read(ref requestedVersion))
                {
                    return;
                }

                try
                {
                    using var lockedGl = MakeCurrent();

                    if (version != Volatile.Read(ref requestedVersion))
                    {
                        return;
                    }

                    foreach (var node in modelNodes)
                    {
                        Scene.Remove(node, dynamic: true);
                    }

                    foreach (var node in particleNodes)
                    {
                        Scene.Remove(node, dynamic: true);
                        node.Delete();
                    }

                    modelNodes.Clear();
                    particleNodes.Clear();

                    AddModels(previewModels);
                    Scene.Initialize();

                    if (frameCamera)
                    {
                        FrameModels();
                    }
                }
                catch (Exception e)
                {
                    Log.Error(nameof(GLCharacterPreviewViewer), $"Failed to load preview models: {e}");
                }
            });
        }

        private void AddModels(IReadOnlyList<PreviewModel> previewModels)
        {
            foreach (var previewModel in previewModels)
            {
                // Owned by the context's resource cache
                var resource = GuiContext.LoadFileCompiled(previewModel.Path);

                if (resource?.DataBlock is not Model model)
                {
                    Log.Warn(nameof(GLCharacterPreviewViewer), $"Could not load \"{previewModel.Path}\" for the preview");
                    continue;
                }

                var node = new ModelSceneNode(Scene, model);

                if (previewModel.Skin > 0 && model.GetMaterialGroups().ElementAtOrDefault(previewModel.Skin).Name is { } skin)
                {
                    node.SetMaterialGroup(skin);
                }

                if (previewModel.BodyGroups is { Count: > 0 } bodyGroups)
                {
                    SetBodyGroups(node, model, bodyGroups);
                }

                if (modelNodes.Count == 0)
                {
                    PlayIdle(node);
                }
                else
                {
                    // Worn the way the game attaches items, following the hero's bones
                    node.SetBoneMergeTarget(modelNodes[0]);
                }

                Scene.Add(node, dynamic: true);

                if (previewModel.Particles != null)
                {
                    AddParticles(node, model, previewModel.Particles, modelNodes.Count > 0 ? modelNodes[0] : null);
                }

                modelNodes.Add(node);
            }
        }

        /// <summary>
        /// Plays the effects the model creates itself, and the given ones the way the game plays an effect an item
        /// creates on its model.
        /// </summary>
        /// <param name="hero">The model the item is worn by, which some effects also follow, or null for the hero itself.</param>
        private void AddParticles(ModelSceneNode node, Model model, IReadOnlyList<string> particles, ModelSceneNode? hero)
        {
            foreach (var particleNode in ParticleSceneNode.CreateModelParticles(Scene, model, node))
            {
                Scene.Add(particleNode, dynamic: true);
                particleNodes.Add(particleNode);
            }

            foreach (var particle in particles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    // Owned by the context's resource cache
                    if (GuiContext.LoadFileCompiled(particle)?.DataBlock is not ParticleSystem particleSystem)
                    {
                        Log.Warn(nameof(GLCharacterPreviewViewer), $"Could not load \"{particle}\" for the preview");
                        continue;
                    }

                    var particleNode = new ParticleSceneNode(Scene, particleSystem)
                    {
                        Name = particle,
                    };

                    particleNode.AttachToModel(node, hero);
                    Scene.Add(particleNode, dynamic: true);
                    particleNodes.Add(particleNode);
                }
                catch (Exception e)
                {
                    Log.Warn(nameof(GLCharacterPreviewViewer), $"Failed to play \"{particle}\" in the preview: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Switches body groups to the given choices, the way the game does for equipped items. Models with fewer
        /// choices show their last one for higher choices.
        /// </summary>
        private static void SetBodyGroups(ModelSceneNode node, Model model, IReadOnlyDictionary<string, int> choices)
        {
            var active = new HashSet<string>(node.GetActiveMeshGroups());

            foreach (var bodyGroup in model.MeshGroups.BodyGroups)
            {
                if (bodyGroup.Choices.Count < 2 || !choices.TryGetValue(bodyGroup.Name, out var choice))
                {
                    continue;
                }

                active.ExceptWith(bodyGroup.Choices.Select(static option => option.FullName));
                active.Add(bodyGroup.Choices[Math.Clamp(choice, 0, bodyGroup.Choices.Count - 1)].FullName);
            }

            node.SetActiveMeshGroups(active);
        }

        /// <summary>
        /// Plays the hero's plain idle. Weapons in particular are only in hand once animated, in the bind pose their bones
        /// sit at the hero's feet.
        /// </summary>
        private static void PlayIdle(ModelSceneNode hero)
        {
            var idle = hero.Animations.Values
                .OfType<SequenceAnimation>()
                .Where(static animation => animation.Activities.Length > 0 && animation.Activities[0].Name == "ACT_DOTA_IDLE")
                .OrderBy(static animation => animation.Activities.Length)
                .ThenByDescending(static animation => animation.Activities[0].Weight)
                .FirstOrDefault();

            if (idle != null)
            {
                hero.SetAnimation(idle);
            }
            else if (hero.Animations.TryGetValue("idle", out var namedIdle))
            {
                hero.SetAnimation(namedIdle);
            }
        }

        /// <summary>
        /// Looks at the hero from the front, the way heroes face in Source 2 (+X). Items are left out, a pet or a
        /// courier would pull the camera away from the hero.
        /// </summary>
        private void FrameModels()
        {
            if (modelNodes.Count == 0)
            {
                return;
            }

            var bounds = modelNodes[0].BoundingBox;
            var distance = Math.Clamp(bounds.Size.Length() * 0.9f, 50f, 2000f);

            Input.Camera.SetLocation(bounds.Center + new Vector3(distance, distance * 0.35f, distance * 0.3f));
            Input.Camera.LookAt(bounds.Center);
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            FrameModels();
        }
    }
}
