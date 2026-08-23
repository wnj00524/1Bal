using System;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Damage.Scenarios;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Tactical;
using TacticalSim.Core.World;

namespace TacticalSim.Core.DependencyInjection
{
    /// <summary>
    /// Extension methods for setting up TacticalSim core simulation services in an <see cref="IServiceCollection"/>.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers all core TacticalSim simulation services, physics models, registries, and turn resolvers.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddTacticalSimCore(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddDeterministicRandomness();
            services.AddDamageModel();
            services.AddMaterialPenetration();
            services.AddSimulationServices();
            services.TryAddSingleton<CapabilityActionPolicy>();
            services.TryAddSingleton<CasualtyBehaviorPolicy>();
            services.TryAddSingleton<TeammateResponsePolicy>();
            services.TryAddSingleton<CasualtyOverlayFactory>();
            services.TryAddSingleton<CasualtyScenarioScorer>();
            services.AddSingleton<IDragModel>(sp => new StandardDragCurve(0.3f));
            services.AddSingleton<IEnvironmentModel>(sp => new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0)));

            return services;
        }

        /// <summary>
        /// Registers the model-version feature flag and the single authoritative
        /// projectile-interaction service.
        /// </summary>
        public static IServiceCollection AddDamageModel(
            this IServiceCollection services,
            DamageModelOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddSingleton(options ?? new DamageModelOptions());
            services.TryAddSingleton<ILesionGenerator, LesionGenerator>();
            services.TryAddSingleton<IMusculoskeletalFunctionalResolver, MusculoskeletalFunctionalResolver>();
            services.TryAddSingleton<INeurologicalFunctionalResolver, NeurologicalFunctionalResolver>();
            services.TryAddSingleton<IProjectileInteractionService, ProjectileInteractionService>();
            services.TryAddSingleton<IReferenceImpactScenarioCatalog>(
                _ => new ReferenceImpactScenarioCatalog());
            services.TryAddTransient<IReferenceImpactRunner, ReferenceImpactRunner>();
            return services;
        }

        /// <summary>
        /// Registers the deterministic root seed and named-stream provider. Register a custom
        /// <see cref="IRootSeedProvider"/> before calling this method to supply a scenario seed.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddDeterministicRandomness(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // Zero is a deterministic fallback for scaffolding. Scenario/replay composition roots
            // should inject their recorded seed before AddTacticalSimCore is called.
            services.TryAddSingleton<IRootSeedProvider>(_ => new FixedRootSeedProvider(0UL));
            services.TryAddSingleton<IDeterministicRandomStreamProvider, DeterministicRandomStreamProvider>();

            return services;
        }

        /// <summary>
        /// Registers terminal ballistics and material penetration services.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddMaterialPenetration(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IMaterialRegistry, MaterialRegistry>();
            services.AddTransient<IMaterialPenetrationSystem, MaterialPenetrationSystem>();
            services.AddTransient<ICoverTrajectorySolver, CoverTrajectorySolver>();

            return services;
        }

        /// <summary>
        /// Registers simultaneous turn resolution and simulation timeline services.
        /// </summary>
        /// <param name="services">The service collection to register services into.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddSimulationServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ITacticalWorld>(_ => new TacticalWorld(WorldBounds.CreateDefault()));
            services.AddTransient<ITurnResolver>(provider =>
                new TurnResolver(provider.GetRequiredService<ITacticalWorld>()));

            return services;
        }
    }
}
