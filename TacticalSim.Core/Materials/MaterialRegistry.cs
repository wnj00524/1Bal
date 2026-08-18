using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TacticalSim.Core.Materials
{
    /// <summary>
    /// Thread-safe registry holding standard and dynamically registered material definitions.
    /// </summary>
    public class MaterialRegistry : IMaterialRegistry
    {
        private readonly ConcurrentDictionary<string, MaterialProperties> _byName =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<MaterialType, MaterialProperties> _byType =
            new();

        public MaterialRegistry()
        {
            RegisterStandardMaterials();
        }

        private void RegisterStandardMaterials()
        {
            // Standard material presets with physically validated constants
            RegisterInternal(new MaterialProperties(
                name: "Wood",
                type: MaterialType.Wood,
                density: 600.0f,
                resistanceCoefficient: 1.0f,
                ricochetAngleThreshold: 1.48f,
                yieldEnergyThreshold: 50.0f));

            RegisterInternal(new MaterialProperties(
                name: "Concrete",
                type: MaterialType.Concrete,
                density: 2400.0f,
                resistanceCoefficient: 1.8f,
                ricochetAngleThreshold: 1.31f,
                yieldEnergyThreshold: 200.0f));

            RegisterInternal(new MaterialProperties(
                name: "Steel",
                type: MaterialType.Steel,
                density: 7850.0f,
                resistanceCoefficient: 2.5f,
                ricochetAngleThreshold: 1.22f,
                yieldEnergyThreshold: 500.0f));

            RegisterInternal(new MaterialProperties(
                name: "Glass",
                type: MaterialType.Glass,
                density: 2500.0f,
                resistanceCoefficient: 0.5f,
                ricochetAngleThreshold: 1.48f,
                yieldEnergyThreshold: 20.0f));

            RegisterInternal(new MaterialProperties(
                name: "Drywall",
                type: MaterialType.Drywall,
                density: 800.0f,
                resistanceCoefficient: 0.4f,
                ricochetAngleThreshold: 1.52f,
                yieldEnergyThreshold: 10.0f));

            RegisterInternal(new MaterialProperties(
                name: "Sand",
                type: MaterialType.Sand,
                density: 1600.0f,
                resistanceCoefficient: 1.5f,
                ricochetAngleThreshold: 1.55f,
                yieldEnergyThreshold: 30.0f));

            RegisterInternal(new MaterialProperties(
                name: "Kevlar",
                type: MaterialType.Kevlar,
                density: 1440.0f,
                resistanceCoefficient: 3.2f,
                ricochetAngleThreshold: 1.48f,
                yieldEnergyThreshold: 100.0f));
        }

        private void RegisterInternal(MaterialProperties material)
        {
            if (string.IsNullOrWhiteSpace(material.Name))
            {
                throw new ArgumentException("Material name cannot be null or empty.", nameof(material));
            }

            _byName[material.Name] = material;
            if (material.Type != MaterialType.Custom)
            {
                _byType[material.Type] = material;
            }
        }

        public void RegisterMaterial(MaterialProperties material)
        {
            RegisterInternal(material);
        }

        public MaterialProperties GetMaterial(MaterialType type)
        {
            if (_byType.TryGetValue(type, out var material))
            {
                return material;
            }

            throw new KeyNotFoundException($"Material of type '{type}' is not registered.");
        }

        public MaterialProperties GetMaterial(string name)
        {
            if (TryGetMaterial(name, out var material))
            {
                return material;
            }

            throw new KeyNotFoundException($"Material with name '{name}' is not registered.");
        }

        public bool TryGetMaterial(string name, out MaterialProperties material)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                material = default;
                return false;
            }

            if (_byName.TryGetValue(name, out material))
            {
                return true;
            }

            if (Enum.TryParse<MaterialType>(name, true, out var parsedType) &&
                _byType.TryGetValue(parsedType, out material))
            {
                return true;
            }

            material = default;
            return false;
        }
    }
}
