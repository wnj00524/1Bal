using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using TacticalSim.Core.Damage;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Units;

namespace TacticalSim.Tests;

public sealed class WoundTrackContractTests
{
    [Fact]
    public void PassageTrack_PreservesOrderedStructuresAndExitSemantics()
    {
        WoundTrackSegment first = CreateSegment(
            0,
            "thorax.skin.anterior",
            "thorax",
            "skin",
            Vector3.Zero,
            new Vector3(0f, 0f, 0.01f),
            100f,
            10f,
            90f);
        WoundTrackSegment second = CreateSegment(
            1,
            "thorax.lung.left",
            "thorax",
            "lung",
            first.EndPoint,
            new Vector3(0f, 0f, 0.08f),
            90f,
            50f,
            40f);

        WoundTrack track = CreateTrack(
            ProjectileDisposition.Exited,
            second.EndPoint,
            null,
            new[] { first, second });

        Assert.False(track.IsRetained);
        Assert.Equal(second.EndPoint, track.ExitPoint);
        Assert.Null(track.RetainedPoint);
        Assert.Equal(new[] { "thorax.skin.anterior", "thorax.lung.left" },
            track.Segments.Select(segment => segment.StructureId));
        Assert.Equal(new[] { 0, 1 }, track.Segments.Select(segment => segment.Sequence));
        Assert.Equal(WoundTrackCoordinateSpace.BodyLocalMeters, track.CoordinateSpace);
    }

    [Fact]
    public void RetainedTrack_RequiresRetainedPointAndNoExitPoint()
    {
        WoundTrackSegment segment = CreateSegment(
            0,
            "pelvis.bone.left",
            "pelvis",
            "bone",
            Vector3.Zero,
            new Vector3(0.02f, 0f, 0f),
            200f,
            200f,
            0f,
            ProjectileStateChangeKind.Retained);

        WoundTrack retained = CreateTrack(
            ProjectileDisposition.Retained,
            null,
            segment.EndPoint,
            new[] { segment });

        Assert.True(retained.IsRetained);
        Assert.Null(retained.ExitPoint);
        Assert.Equal(segment.EndPoint, retained.RetainedPoint);

        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Retained,
            segment.EndPoint,
            null,
            new[] { segment }));

        WoundTrackSegment stillMoving = CreateSegment(
            0,
            "pelvis.bone.left",
            "pelvis",
            "bone",
            Vector3.Zero,
            new Vector3(0.02f, 0f, 0f),
            200f,
            100f,
            100f);
        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Retained,
            null,
            stillMoving.EndPoint,
            new[] { stillMoving }));
    }

    [Fact]
    public void FragmentTracks_AreExplicitOrderedAndImmutable()
    {
        WoundTrackSegment mainSegment = CreateSegment(
            0,
            "thorax.rib.4.left",
            "thorax",
            "bone",
            Vector3.Zero,
            new Vector3(0f, 0f, 0.02f),
            300f,
            100f,
            180f,
            ProjectileStateChangeKind.Fragmented);
        WoundTrackSegment fragmentSegment = CreateSegment(
            0,
            "thorax.lung.left",
            "thorax",
            "lung",
            mainSegment.EndPoint,
            new Vector3(0.03f, 0f, 0.05f),
            20f,
            20f,
            0f,
            ProjectileStateChangeKind.Retained);
        var fragments = new List<FragmentTrack>
        {
            new(
                0,
                "track-001.fragment-000",
                mainSegment.EndPoint,
                ProjectileDisposition.Retained,
                null,
                fragmentSegment.EndPoint,
                Energy.FromJoules(20f),
                Energy.FromJoules(0f),
                new[] { fragmentSegment })
        };

        WoundTrack track = CreateTrack(
            ProjectileDisposition.Exited,
            mainSegment.EndPoint,
            null,
            new[] { mainSegment },
            fragments);
        fragments.Clear();

        FragmentTrack fragment = Assert.Single(track.FragmentTracks);
        Assert.Equal(0, fragment.Sequence);
        Assert.True(fragment.IsRetained);
        Assert.Equal("track-001.fragment-000", fragment.FragmentId);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<FragmentTrack>)track.FragmentTracks).Add(fragment));
        Assert.Throws<ArgumentException>(() => new FragmentTrack(
            0,
            "track-001.fragment-invalid",
            mainSegment.EndPoint,
            ProjectileDisposition.Retained,
            null,
            fragmentSegment.EndPoint,
            Energy.FromJoules(25f),
            Energy.FromJoules(0f),
            new[] { fragmentSegment }));
    }

    [Fact]
    public void Track_RejectsOutOfOrderSegmentsAndFragmentsButAllowsAnatomicalGaps()
    {
        WoundTrackSegment first = CreateSegment(
            0,
            "thorax.skin",
            "thorax",
            "skin",
            Vector3.Zero,
            Vector3.UnitZ,
            10f,
            2f,
            8f);
        WoundTrackSegment wrongSequence = CreateSegment(
            2,
            "thorax.lung",
            "thorax",
            "lung",
            Vector3.UnitZ,
            Vector3.UnitZ * 2f,
            8f,
            4f,
            4f);
        WoundTrackSegment separatedByAirGap = CreateSegment(
            1,
            "thorax.lung",
            "thorax",
            "lung",
            Vector3.UnitX,
            Vector3.UnitZ * 2f,
            8f,
            4f,
            4f);

        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Exited,
            wrongSequence.EndPoint,
            null,
            new[] { first, wrongSequence }));
        WoundTrack gapTrack = CreateTrack(
            ProjectileDisposition.Exited,
            separatedByAirGap.EndPoint,
            null,
            new[] { first, separatedByAirGap });
        Assert.Equal(Vector3.UnitX, gapTrack.Segments[1].EntryPoint);

        var outOfOrderFragment = new FragmentTrack(
            1,
            "fragment-1",
            first.EntryPoint,
            ProjectileDisposition.Exited,
            first.EndPoint,
            null,
            first.IncomingEnergy,
            first.OutgoingEnergy,
            new[] { first });
        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Exited,
            first.EndPoint,
            null,
            new[] { first },
            new[] { outOfOrderFragment }));
    }

    [Fact]
    public void FoundationsTrackRejectsContradictorySegmentEnergyAndDiscontinuousHistory()
    {
        WoundTrackSegment overAllocated = CreateSegment(
            0,
            "thorax.skin",
            "thorax",
            "skin",
            Vector3.Zero,
            Vector3.UnitZ,
            incoming: 100f,
            transferred: 90f,
            outgoing: 90f);
        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Exited,
            overAllocated.EndPoint,
            null,
            new[] { overAllocated }));

        WoundTrackSegment first = CreateSegment(
            0,
            "thorax.skin",
            "thorax",
            "skin",
            Vector3.Zero,
            Vector3.UnitZ,
            incoming: 100f,
            transferred: 10f,
            outgoing: 90f);
        WoundTrackSegment discontinuous = CreateSegment(
            1,
            "thorax.lung",
            "thorax",
            "lung",
            Vector3.UnitZ,
            Vector3.UnitZ * 2f,
            incoming: 80f,
            transferred: 10f,
            outgoing: 70f);
        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Exited,
            discontinuous.EndPoint,
            null,
            new[] { first, discontinuous }));
    }

    [Fact]
    public void SegmentRejectsPathLengthThatDoesNotMatchItsEndpoints()
    {
        Energy incoming = Energy.FromJoules(100f);
        Energy outgoing = Energy.FromJoules(90f);
        var state = new ProjectileStateChange(
            0,
            ProjectileStateChangeKind.Unchanged,
            Vector3.UnitZ,
            Vector3.UnitZ,
            Vector3.UnitZ,
            incoming,
            outgoing);

        Assert.Throws<ArgumentException>(() => new WoundTrackSegment(
            0,
            "thorax.skin",
            "thorax",
            "skin",
            Vector3.Zero,
            Vector3.UnitZ,
            Distance.FromMeters(2f),
            incoming,
            Energy.FromJoules(10f),
            outgoing,
            state));
    }

    [Fact]
    public void DeserializationRejectsLedgerThatDisagreesWithItsSegments()
    {
        WoundTrackSegment segment = CreateSegment(
            0,
            "thorax.skin",
            "thorax",
            "skin",
            Vector3.Zero,
            Vector3.UnitZ,
            incoming: 100f,
            transferred: 10f,
            outgoing: 90f);
        WoundTrack track = CreateTrack(
            ProjectileDisposition.Exited,
            segment.EndPoint,
            null,
            new[] { segment });
        JsonSerializerOptions options = DamageModelJson.CreateOptions();
        JsonNode root = JsonNode.Parse(JsonSerializer.Serialize(track, options))!;
        root["energyLedger"]!["structureDeposits"]![0]!["depositedEnergy"]!["joules"] = 9f;

        Assert.ThrowsAny<ArgumentException>(() =>
            JsonSerializer.Deserialize<WoundTrack>(root.ToJsonString(), options));
    }

    [Fact]
    public void ConservedTrackRejectsPrimaryAndFragmentErrorsThatCancelInAggregate()
    {
        WoundTrackSegment primary = CreateSegment(
            0,
            "thorax.rib",
            "thorax",
            "bone",
            Vector3.Zero,
            Vector3.UnitZ,
            incoming: 100f,
            transferred: 40f,
            outgoing: 50f,
            ProjectileStateChangeKind.Fragmented);
        WoundTrackSegment fragmentSegment = CreateSegment(
            0,
            "thorax.lung",
            "thorax",
            "lung",
            Vector3.UnitZ,
            Vector3.UnitZ * 2f,
            incoming: 20f,
            transferred: 0f,
            outgoing: 10f);
        var fragment = new FragmentTrack(
            0,
            "track.fragment-0",
            fragmentSegment.EntryPoint,
            ProjectileDisposition.Exited,
            fragmentSegment.EndPoint,
            null,
            Energy.FromJoules(20f),
            Energy.FromJoules(10f),
            new[] { fragmentSegment });

        // Aggregate ledger: 100 J = 60 J terminal + 40 J deposited.
        // Locally, the fragment silently loses 10 J and the primary claims 20 J
        // of fragment energy despite leaving only 10 J after its own deposit.
        Assert.Throws<ArgumentException>(() => CreateTrack(
            ProjectileDisposition.Exited,
            primary.EndPoint,
            null,
            new[] { primary },
            new[] { fragment }));
    }

    [Fact]
    public void Json_RoundTripsExactCanonicalTrackWithoutPhysicsReplay()
    {
        WoundTrackSegment main = CreateSegment(
            0,
            "thorax.rib.4.left",
            "thorax",
            "bone",
            new Vector3(0.125f, 0.25f, -0.5f),
            new Vector3(0.125f, 0.25f, -0.48f),
            500f,
            120f,
            350f,
            ProjectileStateChangeKind.Fragmented);
        WoundTrackSegment fragmentSegment = CreateSegment(
            0,
            "thorax.lung.left",
            "thorax",
            "lung",
            main.EndPoint,
            new Vector3(0.15f, 0.25f, -0.44f),
            30f,
            30f,
            0f,
            ProjectileStateChangeKind.Retained);
        var fragment = new FragmentTrack(
            0,
            "shot-42.fragment-0",
            main.EndPoint,
            ProjectileDisposition.Retained,
            null,
            fragmentSegment.EndPoint,
            Energy.FromJoules(30f),
            Energy.FromJoules(0f),
            new[] { fragmentSegment });
        WoundTrack original = CreateTrack(
            ProjectileDisposition.Exited,
            main.EndPoint,
            null,
            new[] { main },
            new[] { fragment });
        JsonSerializerOptions options = DamageModelJson.CreateOptions();

        string json = JsonSerializer.Serialize(original, options);
        WoundTrack restored = JsonSerializer.Deserialize<WoundTrack>(json, options)!;
        string roundTrippedJson = JsonSerializer.Serialize(restored, options);

        Assert.Equal(json, roundTrippedJson);
        Assert.Equal(original.EntryPoint, restored.EntryPoint);
        Assert.Equal(original.Segments[0].PathLength, restored.Segments[0].PathLength);
        Assert.Equal(original.Segments[0].IncomingEnergy, restored.Segments[0].IncomingEnergy);
        Assert.Equal(original.FragmentTracks[0].RetainedPoint, restored.FragmentTracks[0].RetainedPoint);
        Assert.Contains("\"joules\"", json, StringComparison.Ordinal);
        Assert.Contains("\"meters\"", json, StringComparison.Ordinal);
        Assert.Contains("\"modelVersion\":\"m5-foundations-v2\"", json, StringComparison.Ordinal);
        Assert.Contains("\"coordinateSpace\":\"bodyLocalMeters\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DamageContracts_UseSystemNumericsAndCoreHasNoGodotReference()
    {
        Assembly core = typeof(WoundTrack).Assembly;
        Assert.DoesNotContain(core.GetReferencedAssemblies(), reference =>
            reference.Name?.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) == true);

        Type[] vectorProperties =
        [
            typeof(WoundTrack).GetProperty(nameof(WoundTrack.EntryPoint))!.PropertyType,
            typeof(WoundTrackSegment).GetProperty(nameof(WoundTrackSegment.EntryPoint))!.PropertyType,
            typeof(ProjectileStateChange).GetProperty(nameof(ProjectileStateChange.Position))!.PropertyType
        ];
        Assert.All(vectorProperties, propertyType => Assert.Equal(typeof(Vector3), propertyType));
    }

    private static WoundTrackSegment CreateSegment(
        int sequence,
        string structureId,
        string bodyRegion,
        string structureType,
        Vector3 entry,
        Vector3 end,
        float incoming,
        float transferred,
        float outgoing,
        ProjectileStateChangeKind kind = ProjectileStateChangeKind.Unchanged)
    {
        Energy incomingEnergy = Energy.FromJoules(incoming);
        Energy outgoingEnergy = Energy.FromJoules(outgoing);
        var state = new ProjectileStateChange(
            sequence,
            kind,
            end,
            Vector3.UnitZ,
            kind == ProjectileStateChangeKind.Retained ? Vector3.Zero : Vector3.UnitZ,
            incomingEnergy,
            outgoingEnergy);

        return new WoundTrackSegment(
            sequence,
            structureId,
            bodyRegion,
            structureType,
            entry,
            end,
            Distance.FromMeters(Vector3.Distance(entry, end)),
            incomingEnergy,
            Energy.FromJoules(transferred),
            outgoingEnergy,
            state);
    }

    private static WoundTrack CreateTrack(
        ProjectileDisposition disposition,
        Vector3? exitPoint,
        Vector3? retainedPoint,
        IReadOnlyList<WoundTrackSegment> segments,
        IReadOnlyList<FragmentTrack>? fragments = null)
    {
        IReadOnlyList<FragmentTrack> fragmentTracks = fragments ?? Array.Empty<FragmentTrack>();
        Energy incoming = segments[0].IncomingEnergy;
        Energy outgoing = Energy.FromJoules(
            segments[^1].OutgoingEnergy.Joules
            + fragmentTracks.Sum(fragment => fragment.FinalEnergy.Joules));
        WoundTrackSegment[] depositedSegments = segments
            .Concat(fragmentTracks.SelectMany(fragment => fragment.Segments))
            .ToArray();
        var deposits = depositedSegments
            .Select((segment, index) => new EnergyDeposit(
                index,
                segment.StructureId,
                segment.TransferredEnergy))
            .ToArray();
        var ledger = new EnergyLedger(
            incoming,
            outgoing,
            deposits,
            Energy.FromJoules(0f),
            Energy.FromJoules(0f));

        return new WoundTrack(
            "shot-42.primary",
            DamageModelVersion.FoundationsV2,
            WoundTrackCoordinateSpace.BodyLocalMeters,
            segments[0].EntryPoint,
            disposition,
            exitPoint,
            retainedPoint,
            segments,
            fragmentTracks,
            ledger);
    }
}
