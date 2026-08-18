using System.Numerics;
using TacticalSim.Core.Ballistics;

namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Service contract for simulating terminal ballistics, material penetration, velocity loss, and ricochets through environmental barriers.
    /// </summary>
    public interface IMaterialPenetrationSystem
    {
        /// <summary>
        /// Calculates penetration through a planar material slab defined by a nominal thickness and surface normal.
        /// </summary>
        /// <param name="projectile">The incident projectile state.</param>
        /// <param name="profile">The ballistic profile containing projectile mass and cross-sectional area.</param>
        /// <param name="material">The material properties of the barrier.</param>
        /// <param name="nominalThickness">The orthogonal nominal thickness of the slab in meters.</param>
        /// <param name="surfaceNormal">The surface normal of the impacted barrier face.</param>
        /// <returns>The calculated penetration result containing kinematics and energy breakdown.</returns>
        PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            float nominalThickness,
            Vector3 surfaceNormal);

        /// <summary>
        /// Calculates penetration through a barrier between explicit 3D entry and exit points.
        /// </summary>
        /// <param name="projectile">The incident projectile state.</param>
        /// <param name="profile">The ballistic profile containing projectile mass and cross-sectional area.</param>
        /// <param name="material">The material properties of the barrier.</param>
        /// <param name="entryPoint">The 3D point of entry into the material.</param>
        /// <param name="exitPoint">The 3D point of exit out of the material.</param>
        /// <param name="surfaceNormal">The surface normal at the impact face.</param>
        /// <returns>The calculated penetration result containing kinematics and energy breakdown.</returns>
        PenetrationResult CalculatePenetration(
            in ProjectileState projectile,
            in BallisticProfile profile,
            in MaterialProperties material,
            Vector3 entryPoint,
            Vector3 exitPoint,
            Vector3 surfaceNormal);
    }
}
