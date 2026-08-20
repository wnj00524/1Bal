using System;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Cover;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Simulation;
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

            services.AddMaterialPenetration();
            services.AddSimulationServices();
            services.AddSingleton<IDragModel>(sp => new StandardDragCurve(0.3f));
            services.AddSingleton<IEnvironmentModel>(sp => new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0, -9.80665f, 0)));

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
