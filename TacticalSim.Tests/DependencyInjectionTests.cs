using System;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Materials;
using TacticalSim.Core.Simulation;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    public class DependencyInjectionTests
    {
        #region Core Registration & Resolution Tests

        [Fact]
        public void AddTacticalSimCore_RegistersAndResolvesAllRequiredInterfaces()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            // Assert
            var materialRegistry = provider.GetService<IMaterialRegistry>();
            Assert.NotNull(materialRegistry);
            Assert.IsType<MaterialRegistry>(materialRegistry);

            var penetrationSystem = provider.GetService<IMaterialPenetrationSystem>();
            Assert.NotNull(penetrationSystem);
            Assert.IsType<MaterialPenetrationSystem>(penetrationSystem);

            var turnResolver = provider.GetService<ITurnResolver>();
            Assert.NotNull(turnResolver);
            Assert.IsType<TurnResolver>(turnResolver);

            var dragModel = provider.GetService<IDragModel>();
            Assert.NotNull(dragModel);
            Assert.IsType<StandardDragCurve>(dragModel);

            var environmentModel = provider.GetService<IEnvironmentModel>();
            Assert.NotNull(environmentModel);
            Assert.IsType<ICAOStandardAtmosphere>(environmentModel);
        }

        [Fact]
        public void AddTacticalSimCore_FluentChaining_ReturnsSameServiceCollectionInstance()
        {
            var services = new ServiceCollection();
            var returnedServices = services.AddTacticalSimCore();

            Assert.Same(services, returnedServices);
        }

        [Fact]
        public void AddTacticalSimCore_NullServices_ThrowsArgumentNullException()
        {
            IServiceCollection services = null!;
            Assert.Throws<ArgumentNullException>(() => services.AddTacticalSimCore());
        }

        #endregion

        #region Lifetime Semantics Tests

        [Fact]
        public void LifetimeSemantics_SingletonServices_PreserveInstanceAcrossResolutions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            // Act & Assert: MaterialRegistry is Singleton
            var registry1 = provider.GetRequiredService<IMaterialRegistry>();
            var registry2 = provider.GetRequiredService<IMaterialRegistry>();
            Assert.Same(registry1, registry2);

            // Act & Assert: DragModel is Singleton
            var drag1 = provider.GetRequiredService<IDragModel>();
            var drag2 = provider.GetRequiredService<IDragModel>();
            Assert.Same(drag1, drag2);

            // Act & Assert: EnvironmentModel is Singleton
            var env1 = provider.GetRequiredService<IEnvironmentModel>();
            var env2 = provider.GetRequiredService<IEnvironmentModel>();
            Assert.Same(env1, env2);
        }

        [Fact]
        public void LifetimeSemantics_TransientServices_YieldNewInstancesAcrossResolutions()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            // Act & Assert: MaterialPenetrationSystem is Transient
            var pen1 = provider.GetRequiredService<IMaterialPenetrationSystem>();
            var pen2 = provider.GetRequiredService<IMaterialPenetrationSystem>();
            Assert.NotSame(pen1, pen2);

            // Act & Assert: TurnResolver is Transient
            var turn1 = provider.GetRequiredService<ITurnResolver>();
            var turn2 = provider.GetRequiredService<ITurnResolver>();
            Assert.NotSame(turn1, turn2);
        }

        [Fact]
        public void LifetimeSemantics_ScopeHierarchy_MaintainsExpectedLifetimes()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var rootProvider = services.BuildServiceProvider();

            using var scope1 = rootProvider.CreateScope();
            using var scope2 = rootProvider.CreateScope();

            // Singletons identical across scopes
            var regScope1 = scope1.ServiceProvider.GetRequiredService<IMaterialRegistry>();
            var regScope2 = scope2.ServiceProvider.GetRequiredService<IMaterialRegistry>();
            Assert.Same(regScope1, regScope2);

            // Transients distinct across scopes
            var turnScope1 = scope1.ServiceProvider.GetRequiredService<ITurnResolver>();
            var turnScope2 = scope2.ServiceProvider.GetRequiredService<ITurnResolver>();
            Assert.NotSame(turnScope1, turnScope2);
        }

        #endregion

        #region Modular Registration Tests

        [Fact]
        public void AddMaterialPenetration_RegistersOnlyMaterialPenetrationServices()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var returnedServices = services.AddMaterialPenetration();
            var provider = services.BuildServiceProvider();

            // Assert
            Assert.Same(services, returnedServices);

            var materialRegistry = provider.GetService<IMaterialRegistry>();
            Assert.NotNull(materialRegistry);
            Assert.IsType<MaterialRegistry>(materialRegistry);

            var penetrationSystem = provider.GetService<IMaterialPenetrationSystem>();
            Assert.NotNull(penetrationSystem);
            Assert.IsType<MaterialPenetrationSystem>(penetrationSystem);

            // Other services must NOT be registered
            Assert.Null(provider.GetService<ITurnResolver>());
            Assert.Null(provider.GetService<IDragModel>());
            Assert.Null(provider.GetService<IEnvironmentModel>());
        }

        [Fact]
        public void AddMaterialPenetration_NullServices_ThrowsArgumentNullException()
        {
            IServiceCollection services = null!;
            Assert.Throws<ArgumentNullException>(() => services.AddMaterialPenetration());
        }

        [Fact]
        public void AddSimulationServices_RegistersOnlySimulationTurnResolver()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var returnedServices = services.AddSimulationServices();
            var provider = services.BuildServiceProvider();

            // Assert
            Assert.Same(services, returnedServices);

            var turnResolver = provider.GetService<ITurnResolver>();
            Assert.NotNull(turnResolver);
            Assert.IsType<TurnResolver>(turnResolver);

            // Other services must NOT be registered
            Assert.Null(provider.GetService<IMaterialRegistry>());
            Assert.Null(provider.GetService<IMaterialPenetrationSystem>());
            Assert.Null(provider.GetService<IDragModel>());
            Assert.Null(provider.GetService<IEnvironmentModel>());
        }

        [Fact]
        public void AddSimulationServices_NullServices_ThrowsArgumentNullException()
        {
            IServiceCollection services = null!;
            Assert.Throws<ArgumentNullException>(() => services.AddSimulationServices());
        }

        #endregion

        #region End-to-End Operational Usage Tests

        [Fact]
        public void EndToEnd_ResolvedTurnResolver_SchedulesAndExecutesAction()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var resolver = provider.GetRequiredService<ITurnResolver>();
            var actorId = Guid.NewGuid();
            bool executed = false;

            var action = new GenericTacticalAction(
                actorId: actorId,
                tuCost: 10f,
                onExecute: dt => executed = true);

            // Act
            resolver.ScheduleAction(action);
            Assert.True(resolver.HasActiveActions);
            Assert.Equal(1, resolver.ActiveActorCount);

            resolver.Tick(10f);

            // Assert
            Assert.True(executed);
            Assert.Equal(10f, resolver.GlobalTime);
            Assert.Equal(TacticalActionState.Completed, action.State);
            Assert.False(resolver.HasActiveActions);
        }

        [Fact]
        public void EndToEnd_ResolvedMaterialPenetration_CalculatesPenetrationAccurately()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var registry = provider.GetRequiredService<IMaterialRegistry>();
            var penetrationSystem = provider.GetRequiredService<IMaterialPenetrationSystem>();

            var wood = registry.GetMaterial("Wood");
            var steel = registry.GetMaterial("Steel");

            var projectile = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 900f), // 900 m/s high-velocity rifle
                Time = 0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.008f, // 8 grams (7.62x39mm / 5.56mm class)
                CrossSectionalArea = 0.000045f,
                DragModel = provider.GetRequiredService<IDragModel>()
            };

            // Act: Thin wood board (2 cm)
            var woodResult = penetrationSystem.CalculatePenetration(
                projectile,
                profile,
                wood,
                nominalThickness: 0.02f,
                surfaceNormal: new Vector3(0, 0, -1));

            // Assert Wood
            Assert.Equal(PenetrationOutcome.Perforated, woodResult.Outcome);
            Assert.True(woodResult.ExitVelocity > 0f);
            Assert.True(woodResult.ExitVelocity < 900f);
            Assert.True(woodResult.RemainingKineticEnergy > 0f);
            Assert.True(woodResult.TransferredKineticEnergy > 0f);

            // Act: Extremely thick armor steel block (50 cm)
            var steelResult = penetrationSystem.CalculatePenetration(
                projectile,
                profile,
                steel,
                nominalThickness: 0.50f,
                surfaceNormal: new Vector3(0, 0, -1));

            // Assert Steel
            Assert.Equal(PenetrationOutcome.Stopped, steelResult.Outcome);
            Assert.Equal(0f, steelResult.ExitVelocity);
            Assert.Equal(0f, steelResult.RemainingKineticEnergy);
            Assert.Equal(woodResult.InitialKineticEnergy, steelResult.InitialKineticEnergy);
        }

        [Fact]
        public void EndToEnd_ResolvedDragAndEnvironmentModels_SimulateBallisticStep()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddTacticalSimCore();
            var provider = services.BuildServiceProvider();

            var dragModel = provider.GetRequiredService<IDragModel>();
            var envModel = provider.GetRequiredService<IEnvironmentModel>();

            // Assert Drag Model curves
            float subsonicCd = dragModel.GetDragCoefficient(0.5f);
            float transonicPeakCd = dragModel.GetDragCoefficient(1.0f);
            float supersonicCd = dragModel.GetDragCoefficient(2.0f);

            Assert.Equal(0.3f, subsonicCd, 3);
            Assert.True(transonicPeakCd > subsonicCd, "Transonic drag must exceed subsonic base drag");
            Assert.True(supersonicCd >= subsonicCd, "Supersonic drag must be at least base drag");

            // Assert Atmosphere Model
            var seaLevel = envModel.GetConditionsAt(Vector3.Zero);
            var highAltitude = envModel.GetConditionsAt(new Vector3(0, 5000f, 0));

            Assert.True(seaLevel.AirDensity > 1.0f && seaLevel.AirDensity < 1.3f);
            Assert.True(highAltitude.AirDensity < seaLevel.AirDensity, "Air density must decrease with altitude");
            Assert.Equal(-9.80665f, seaLevel.Gravity.Y, 4);

            // Act: Ballistic trajectory step
            var initialState = new ProjectileState
            {
                Position = Vector3.Zero,
                Velocity = new Vector3(0, 0, 800f),
                Time = 0f
            };

            var profile = new BallisticProfile
            {
                Mass = 0.01f,
                CrossSectionalArea = 0.00005f,
                DragModel = dragModel
            };

            var nextState = BallisticSolver.StepRK4(initialState, profile, envModel, 0.01f);

            // Assert physics step
            Assert.True(nextState.Position.Z > 0f);
            Assert.True(nextState.Position.Y < 0f, "Gravity should pull projectile downward");
            Assert.True(nextState.Velocity.Z < 800f, "Atmospheric drag should reduce horizontal velocity");
        }

        #endregion
    }
}
