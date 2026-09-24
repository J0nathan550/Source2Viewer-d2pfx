namespace ValveResourceFormat.Particles;

/// <summary>
/// A posed model a control point is attached to, which effects can create their particles on, e.g. the cosmetic item
/// an ambient effect plays on.
/// </summary>
public interface IParticleModel
{
    /// <summary>
    /// Picks a point on the model as it is posed now.
    /// </summary>
    /// <param name="useMesh">Whether to pick a point on the surface of the model's meshes, rather than within one of its hitboxes.</param>
    /// <param name="hitboxSetName">The hitbox set to pick a hitbox from.</param>
    /// <param name="random">Four uniform random numbers in [0, 1) that decide the point, the same numbers pick the same point.</param>
    /// <param name="position">The picked point, in world space.</param>
    /// <returns>Whether the model has anything to pick a point on.</returns>
    bool TryGetPoint(bool useMesh, string hitboxSetName, Vector4 random, out Vector3 position);
}
