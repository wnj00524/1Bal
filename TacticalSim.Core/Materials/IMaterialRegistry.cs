namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Registry contract for querying and dynamically registering barrier and armor material properties.
    /// </summary>
    public interface IMaterialRegistry
    {
        /// <summary>
        /// Retrieves material properties by enum type. Throws KeyNotFoundException if not found.
        /// </summary>
        MaterialProperties GetMaterial(MaterialType type);

        /// <summary>
        /// Retrieves material properties by name (case-insensitive). Throws KeyNotFoundException if not found.
        /// </summary>
        MaterialProperties GetMaterial(string name);

        /// <summary>
        /// Attempts to retrieve material properties by name (or enum name). Returns true if found.
        /// </summary>
        bool TryGetMaterial(string name, out MaterialProperties material);

        /// <summary>
        /// Dynamically registers or updates a material in the registry in a thread-safe manner.
        /// </summary>
        void RegisterMaterial(MaterialProperties material);
    }
}
