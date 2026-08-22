using System;
using System.Numerics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TacticalSim.Core;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.DependencyInjection;
using TacticalSim.Core.Entities;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Simulation.Actions;
using Xunit;

namespace TacticalSim.Tests
{
    public class DeterministicRandomTests
    {
        [Fact]
        public void SameRootSeedAndStreamName_ProduceIdenticalSequences()
        {
            var firstProvider = CreateProvider(123456789UL);
            var secondProvider = CreateProvider(123456789UL);
            var first = firstProvider.GetStream("ballistics.fragmentation");
            var second = secondProvider.GetStream("ballistics.fragmentation");

            for (int draw = 0; draw < 32; draw++)
            {
                Assert.Equal(first.NextUInt64(), second.NextUInt64());
            }

            Assert.Equal(32UL, first.DrawCount);
            Assert.Equal(32UL, second.DrawCount);
        }

        [Fact]
        public void CurrentAlgorithmVersion_MatchesKnownReplayVector()
        {
            var stream = CreateProvider(123456789UL).GetStream("ballistics.fragmentation");

            Assert.Equal(0x0A5EC352D244E8BBUL, stream.StreamSeed);
            Assert.Equal(0xCCF21C8995E2FA94UL, stream.NextUInt64());
            Assert.Equal(0x82B1AD26168036B9UL, stream.NextUInt64());
            Assert.Equal(0x0BE12B5199A8C552UL, stream.NextUInt64());
            Assert.Equal(0x36E6478C56A7BB72UL, stream.NextUInt64());
        }

        [Fact]
        public void NamedStreams_AdvanceIndependentlyOfOtherSubsystems()
        {
            var providerWithUnrelatedDraws = CreateProvider(42UL);
            var cleanProvider = CreateProvider(42UL);

            var unrelated = providerWithUnrelatedDraws.GetStream("physiology.hemostasis");
            for (int draw = 0; draw < 100; draw++)
            {
                unrelated.NextUInt64();
            }

            ulong afterUnrelatedDraws = providerWithUnrelatedDraws
                .GetStream("shooting.deviation")
                .NextUInt64();
            ulong withoutUnrelatedDraws = cleanProvider
                .GetStream("shooting.deviation")
                .NextUInt64();

            Assert.Equal(withoutUnrelatedDraws, afterUnrelatedDraws);
        }

        [Fact]
        public void GetStream_WithSameOrdinalName_ReturnsSameAdvancingSource()
        {
            var provider = CreateProvider(7UL);
            var firstReference = provider.GetStream("casualty.variation");

            firstReference.NextUInt64();
            var secondReference = provider.GetStream("casualty.variation");

            Assert.Same(firstReference, secondReference);
            Assert.Equal(1UL, secondReference.DrawCount);
        }

        [Fact]
        public void DifferentRootSeedsAndNames_ProduceDifferentStreams()
        {
            var rootOne = CreateProvider(1UL);
            var rootTwo = CreateProvider(2UL);

            ulong baseline = rootOne.GetStream("stream.a").NextUInt64();

            Assert.NotEqual(baseline, rootTwo.GetStream("stream.a").NextUInt64());
            Assert.NotEqual(baseline, rootOne.GetStream("stream.b").NextUInt64());
        }

        [Fact]
        public void CaptureSnapshot_RecordsRootSeedSortedStreamsSeedsAndDrawCounts()
        {
            var provider = CreateProvider(987654321UL);
            var zulu = provider.GetStream("zulu");
            var alpha = provider.GetStream("alpha");

            zulu.NextUInt64();
            alpha.NextUnitDouble();
            alpha.NextUInt64();

            var snapshot = provider.CaptureSnapshot();

            Assert.Equal(DeterministicRandomStreamProvider.CurrentAlgorithmVersion, snapshot.AlgorithmVersion);
            Assert.Equal(987654321UL, snapshot.RootSeed);
            Assert.Collection(
                snapshot.Streams,
                stream =>
                {
                    Assert.Equal("alpha", stream.StreamName);
                    Assert.Equal(alpha.StreamSeed, stream.StreamSeed);
                    Assert.Equal(2UL, stream.DrawCount);
                },
                stream =>
                {
                    Assert.Equal("zulu", stream.StreamName);
                    Assert.Equal(zulu.StreamSeed, stream.StreamSeed);
                    Assert.Equal(1UL, stream.DrawCount);
                });

            Assert.Equal(2UL, alpha.DrawCount);
            Assert.Equal(1UL, zulu.DrawCount);
        }

        [Fact]
        public void MetadataSnapshot_RoundTripsThroughJsonForReplayAndDebugStorage()
        {
            var provider = CreateProvider(314159UL);
            provider.GetStream("shooting.deviation").NextUInt64();
            var snapshot = provider.CaptureSnapshot();

            string json = JsonSerializer.Serialize(snapshot);
            var restored = JsonSerializer.Deserialize<DeterministicRandomMetadataSnapshot>(json);

            Assert.NotNull(restored);
            Assert.Equal(snapshot.AlgorithmVersion, restored.AlgorithmVersion);
            Assert.Equal(snapshot.RootSeed, restored.RootSeed);
            var restoredStream = Assert.Single(restored.Streams);
            Assert.Equal(snapshot.Streams[0], restoredStream);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(" leading")]
        [InlineData("trailing ")]
        public void GetStream_InvalidName_ThrowsArgumentException(string streamName)
        {
            var provider = CreateProvider(0UL);

            Assert.Throws<ArgumentException>(() => provider.GetStream(streamName));
        }

        [Fact]
        public void GetStream_NullName_ThrowsArgumentNullException()
        {
            var provider = CreateProvider(0UL);

            Assert.Throws<ArgumentNullException>(() => provider.GetStream(null!));
        }

        [Fact]
        public void Provider_NullRootSeedProvider_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DeterministicRandomStreamProvider(null!));
        }

        [Fact]
        public void NextUnitDouble_AlwaysReturnsHalfOpenUnitInterval()
        {
            var stream = CreateProvider(ulong.MaxValue).GetStream("validation.unit-interval");

            for (int draw = 0; draw < 10_000; draw++)
            {
                double value = stream.NextUnitDouble();
                Assert.True(value >= 0d);
                Assert.True(value < 1d);
            }
        }

        [Fact]
        public void AddTacticalSimCore_UsesInjectedRootSeedAndSingletonStreamProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IRootSeedProvider>(new FixedRootSeedProvider(20260822UL));
            services.AddTacticalSimCore();

            using var serviceProvider = services.BuildServiceProvider();
            var first = serviceProvider.GetRequiredService<IDeterministicRandomStreamProvider>();
            var second = serviceProvider.GetRequiredService<IDeterministicRandomStreamProvider>();

            Assert.Same(first, second);
            Assert.Equal(20260822UL, first.RootSeed);
            Assert.Equal(20260822UL, serviceProvider.GetRequiredService<IRootSeedProvider>().RootSeed);
        }

        [Fact]
        public void ShootTacticalAction_SameSeedScenarioAndActor_ReplaysDeviationExactly()
        {
            Guid recordedActorId = Guid.Parse("2a53c6b7-e2dc-4fa2-91d3-e83ce467fe61");
            var shooter = CreatePainfulShooter(recordedActorId);
            var reconstructedShooter = CreatePainfulShooter(recordedActorId);
            var environment = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0f, -9.80665f, 0f));
            var firstProvider = CreateProvider(8675309UL);
            var replayProvider = CreateProvider(8675309UL);

            var firstAction = new ShootTacticalAction(shooter, Vector3.UnitZ, environment, firstProvider);
            var replayAction = new ShootTacticalAction(
                reconstructedShooter,
                Vector3.UnitZ,
                environment,
                replayProvider);

            firstAction.OnComplete();
            replayAction.OnComplete();

            Assert.Equal(firstAction.FinalState, replayAction.FinalState);

            var snapshot = firstProvider.CaptureSnapshot();
            var stream = Assert.Single(snapshot.Streams);
            Assert.Equal($"shooting.deviation.actor/{shooter.Id:N}", stream.StreamName);
            Assert.Equal(2UL, stream.DrawCount);
        }

        [Fact]
        public void ShootTacticalAction_RepeatedShotsAdvanceTheActorsNamedStream()
        {
            var shooter = CreatePainfulShooter();
            var environment = new ICAOStandardAtmosphere(Vector3.Zero, new Vector3(0f, -9.80665f, 0f));
            var randomStreams = CreateProvider(8675309UL);

            var firstAction = new ShootTacticalAction(shooter, Vector3.UnitZ, environment, randomStreams);
            var secondAction = new ShootTacticalAction(shooter, Vector3.UnitZ, environment, randomStreams);

            firstAction.OnComplete();
            secondAction.OnComplete();

            Assert.NotEqual(firstAction.FinalState, secondAction.FinalState);
            Assert.Equal(4UL, Assert.Single(randomStreams.CaptureSnapshot().Streams).DrawCount);
        }

        [Fact]
        public void ShootTacticalAction_NullRandomProvider_ThrowsArgumentNullException()
        {
            var shooter = CreatePainfulShooter();
            var environment = new ICAOStandardAtmosphere(Vector3.Zero, Vector3.Zero);

            Assert.Throws<ArgumentNullException>(() =>
                new ShootTacticalAction(shooter, Vector3.UnitZ, environment, null!));
        }

        private static DeterministicRandomStreamProvider CreateProvider(ulong rootSeed)
        {
            return new DeterministicRandomStreamProvider(new FixedRootSeedProvider(rootSeed));
        }

        private static TacticalEntity CreatePainfulShooter(Guid? recordedId = null)
        {
            var physiology = new TacticalActorPhysiology();
            var arm = new BodyPart { Type = BodyPartType.LeftArm };
            var voxel = new PhysiologicalVoxel(Vector3.Zero, 0.01f, TissueRegistry.Bone, OrganType.None);
            arm.Voxels.Add(voxel);
            physiology.SetRoot(arm);
            voxel.ApplyKineticEnergy(500f, Vector3.Zero, 0.001f);
            physiology.TickPhysiology(10f);

            var shooter = recordedId.HasValue
                ? new TacticalEntity(recordedId.Value, Vector3.Zero, physiology)
                : new TacticalEntity(Vector3.Zero, physiology);
            shooter.EquippedWeapon = new WeaponProfile
            {
                Name = "Determinism Test Rifle",
                BaseTUCostToFire = 10f,
                LoadedAmmunition = new AmmunitionProfile
                {
                    MuzzleVelocity = 100f,
                    Ballistics = new BallisticProfile
                    {
                        Mass = 0.01f,
                        CrossSectionalArea = 0.01f,
                        DragModel = new StandardDragCurve(1f)
                    }
                }
            };

            Assert.True(physiology.PainLevel > 0.01f);
            return shooter;
        }
    }
}
