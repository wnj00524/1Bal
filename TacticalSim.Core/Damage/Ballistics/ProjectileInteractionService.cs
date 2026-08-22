using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using TacticalSim.Core.Ballistics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Randomness;
using TacticalSim.Core.Units;
using TacticalSim.Core.Damage.Lesions;
using System.Text.Json;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// Authoritative projectile-to-wound boundary. A null result means the supplied
/// body-local ray did not intersect an intact target structure.
/// </summary>
public interface IProjectileInteractionService
{
    ProjectileInteractionResult? Resolve(ProjectileInteractionRequest request);
}

/// <summary>
/// Deterministically resolves ordered body intersections and applies each joule
/// lost by the projectile exactly once to the intersected target structure.
/// </summary>
public sealed class ProjectileInteractionService : IProjectileInteractionService
{
    private const float EnergyStopEpsilonJoules = 1e-7f;
    private const float SpeedEpsilonMetersPerSecond = 1e-6f;

    private readonly DamageModelOptions _options;
    private readonly IDeterministicRandomStreamProvider _randomStreams;
    private readonly ILesionGenerator _lesionGenerator;

    public ProjectileInteractionService(
        DamageModelOptions options,
        IDeterministicRandomStreamProvider randomStreams,
        ILesionGenerator? lesionGenerator = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _randomStreams = randomStreams ?? throw new ArgumentNullException(nameof(randomStreams));
        _lesionGenerator = lesionGenerator ?? new LesionGenerator();
    }

    public ProjectileInteractionResult? Resolve(ProjectileInteractionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        DamageModelVersion version = request.ModelVersion ?? _options.DefaultVersion;
        IReadOnlyList<VoxelRayIntersection> intersections = OrderedVoxelTraversal.FindIntersections(
            request.TargetPhysiology.RootBodyPart,
            request.ProjectileState.Position,
            request.ProjectileState.Velocity,
            request.MaximumTraversalDistance);
        if (intersections.Count == 0)
            return null;

        PhysiologyDebugSnapshot physiologyBefore = PhysiologyDebugSnapshot.Capture(request.TargetPhysiology);
        CapabilityDebugSnapshot capabilityBefore = CapabilityDebugSnapshot.Capture(request.TargetPhysiology);
        IDeterministicRandomStreamProvider randomStreams = GetRandomStreams(request);

        return version switch
        {
            DamageModelVersion.FoundationsV2 => ResolveFoundations(
                request,
                intersections,
                physiologyBefore,
                capabilityBefore,
                randomStreams),
            DamageModelVersion.LegacyV1 => ResolveLegacy(
                request,
                intersections,
                physiologyBefore,
                capabilityBefore,
                randomStreams),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unsupported damage-model version.")
        };
    }

    private ProjectileInteractionResult ResolveFoundations(
        ProjectileInteractionRequest request,
        IReadOnlyList<VoxelRayIntersection> intersections,
        PhysiologyDebugSnapshot physiologyBefore,
        CapabilityDebugSnapshot capabilityBefore,
        IDeterministicRandomStreamProvider randomStreams)
    {
        BallisticProfile profile = request.ProjectileProfile;
        Vector3 currentVelocity = request.ProjectileState.Velocity;
        Energy initialEnergy = KineticEnergy(profile, currentVelocity);
        Energy currentEnergy = initialEnergy;
        float elapsedSeconds = 0f;
        float previousExitDistance = 0f;
        bool terminated = false;
        bool ricocheted = false;
        Vector3 finalPosition = request.ProjectileState.Position;

        var segments = new List<WoundTrackSegment>(intersections.Count);
        var deposits = new List<EnergyDeposit>(intersections.Count);
        var cavitationEffects = new List<CavitationDebugSnapshot>();

        foreach (VoxelRayIntersection intersection in intersections)
        {
            if (currentEnergy.Joules <= EnergyStopEpsilonJoules)
                break;

            float incomingSpeed = currentVelocity.Length();
            Vector3 incomingDirection = Vector3.Normalize(currentVelocity);
            float gapMeters = MathF.Max(0f, intersection.EntryDistanceMeters - previousExitDistance);
            elapsedSeconds += gapMeters / MathF.Max(incomingSpeed, SpeedEpsilonMetersPerSecond);

            Energy incomingEnergy = currentEnergy;
            float nominalPathMeters = intersection.PathLengthMeters;
            float actualPathMeters = nominalPathMeters;
            Vector3 outgoingDirection = incomingDirection;
            Energy outgoingEnergy;
            ProjectileStateChangeKind changeKind = ProjectileStateChangeKind.Unchanged;

            if (intersection.Voxel.Organ == OrganType.Bone)
            {
                BoneImpactResult boneImpact = InternalRicochetSolver.Resolve(
                    currentVelocity,
                    profile,
                    intersection.EntrySurfaceNormal,
                    intersection.Voxel.Tissue,
                    nominalPathMeters);
                float transferredJoules = Math.Clamp(
                    boneImpact.TransferredEnergy,
                    0f,
                    incomingEnergy.Joules);
                outgoingEnergy = Energy.FromJoules(incomingEnergy.Joules - transferredJoules);

                if (boneImpact.Outcome == BoneImpactOutcome.Ricocheted)
                {
                    actualPathMeters = 0f;
                    outgoingDirection = boneImpact.Velocity.LengthSquared() > 0f
                        ? Vector3.Normalize(boneImpact.Velocity)
                        : -incomingDirection;
                    changeKind = ProjectileStateChangeKind.Ricocheted;
                    ricocheted = true;
                    terminated = true;
                }
            }
            else
            {
                float dragCoefficient = MathF.Max(0f, profile.DragModel.GetDragCoefficient(0f));
                float dragForceNewtons = 0.5f
                    * intersection.Voxel.Tissue.MassDensity.KilogramsPerCubicMeter
                    * incomingSpeed * incomingSpeed
                    * dragCoefficient
                    * profile.CrossSectionalAreaSquareMeters.SquareMeters;
                float possibleTransferJoules = dragForceNewtons * nominalPathMeters;
                float transferredJoules = Math.Clamp(possibleTransferJoules, 0f, incomingEnergy.Joules);
                outgoingEnergy = Energy.FromJoules(incomingEnergy.Joules - transferredJoules);

                if (IsEffectivelyStopped(incomingEnergy, outgoingEnergy))
                {
                    outgoingEnergy = Energy.FromJoules(0f);
                    actualPathMeters = dragForceNewtons > 0f
                        ? MathF.Min(nominalPathMeters, incomingEnergy.Joules / dragForceNewtons)
                        : 0f;
                    changeKind = ProjectileStateChangeKind.Retained;
                    terminated = true;
                }
            }

            // Normalize a complete stop for every material branch. In particular,
            // an exact-threshold bone shatter can consume all incoming energy and
            // must not be reported as an exited zero-velocity projectile.
            if (!ricocheted && IsEffectivelyStopped(incomingEnergy, outgoingEnergy))
            {
                outgoingEnergy = Energy.FromJoules(0f);
                changeKind = ProjectileStateChangeKind.Retained;
                terminated = true;
            }

            Energy transferredEnergy = Energy.FromJoules(
                MathF.Max(0f, incomingEnergy.Joules - outgoingEnergy.Joules));
            Vector3 segmentEnd = intersection.EntryPointMeters + incomingDirection * actualPathMeters;
            finalPosition = segmentEnd;

            float outgoingSpeed = outgoingEnergy.Joules > 0f
                ? MathF.Sqrt(2f * outgoingEnergy.Joules / profile.MassKilograms.Kilograms)
                : 0f;
            currentVelocity = outgoingDirection * outgoingSpeed;
            currentEnergy = outgoingEnergy;

            float averageSpeed = (incomingSpeed + outgoingSpeed) * 0.5f;
            elapsedSeconds += actualPathMeters / MathF.Max(averageSpeed, SpeedEpsilonMetersPerSecond);

            if (transferredEnergy.Joules > 0f)
            {
                float directCrushVolume = profile.CrossSectionalAreaSquareMeters.SquareMeters * actualPathMeters;
                Vector3 depositPoint = intersection.EntryPointMeters + incomingDirection * (actualPathMeters * 0.5f);
                CavitationEvent? cavitation = intersection.Voxel.ApplyKineticEnergy(
                    transferredEnergy.Joules,
                    depositPoint,
                    directCrushVolume);
                if (cavitation.HasValue)
                {
                    cavitationEffects.Add(new CavitationDebugSnapshot(
                        intersection.StructureId,
                        cavitation.Value.Origin,
                        Distance.FromMeters(cavitation.Value.Radius),
                        Energy.FromJoules(cavitation.Value.Energy)));
                }
            }

            int sequence = segments.Count;
            var stateChange = new ProjectileStateChange(
                sequence,
                changeKind,
                segmentEnd,
                incomingDirection,
                outgoingEnergy.Joules > 0f ? outgoingDirection : Vector3.Zero,
                incomingEnergy,
                outgoingEnergy);
            segments.Add(new WoundTrackSegment(
                sequence,
                intersection.StructureId,
                intersection.BodyRegion,
                intersection.StructureType,
                intersection.EntryPointMeters,
                segmentEnd,
                Distance.FromMeters(actualPathMeters),
                incomingEnergy,
                transferredEnergy,
                outgoingEnergy,
                stateChange));
            deposits.Add(new EnergyDeposit(sequence, intersection.StructureId, transferredEnergy));

            previousExitDistance = intersection.ExitDistanceMeters;
            if (terminated)
                break;
        }

        if (segments.Count == 0)
            throw new InvalidOperationException("An intersected projectile produced no wound-track segment.");

        var ledger = new EnergyLedger(
            initialEnergy,
            currentEnergy,
            deposits,
            Energy.FromJoules(0f),
            Energy.FromJoules(0f));
        ProjectileDisposition disposition = terminated && !ricocheted
            ? ProjectileDisposition.Retained
            : ProjectileDisposition.Exited;
        Vector3? exitPoint = disposition == ProjectileDisposition.Exited ? finalPosition : null;
        Vector3? retainedPoint = disposition == ProjectileDisposition.Retained ? finalPosition : null;
        var woundTrack = new WoundTrack(
            request.ImpactId,
            DamageModelVersion.FoundationsV2,
            WoundTrackCoordinateSpace.BodyLocalMeters,
            segments[0].EntryPoint,
            disposition,
            exitPoint,
            retainedPoint,
            segments,
            Array.Empty<FragmentTrack>(),
            ledger);

        var finalState = new ProjectileState
        {
            Position = finalPosition,
            Velocity = currentVelocity,
            Time = request.ProjectileState.Time + elapsedSeconds
        };
        return BuildResult(
            request,
            finalState,
            woundTrack,
            physiologyBefore,
            capabilityBefore,
            randomStreams,
            cavitationEffects,
            ledger.ConservationWarning is null ? [] : [ledger.ConservationWarning]);
    }

    private ProjectileInteractionResult ResolveLegacy(
        ProjectileInteractionRequest request,
        IReadOnlyList<VoxelRayIntersection> intersections,
        PhysiologyDebugSnapshot physiologyBefore,
        CapabilityDebugSnapshot capabilityBefore,
        IDeterministicRandomStreamProvider randomStreams)
    {
        Vector3 direction = Vector3.Normalize(request.ProjectileState.Velocity);
        Energy incomingEnergy = KineticEnergy(request.ProjectileProfile, request.ProjectileState.Velocity);
        IReadOnlyList<BodyVoxelReference> voxelReferences =
            OrderedVoxelTraversal.EnumerateVoxels(request.TargetPhysiology.RootBodyPart);
        var beforeEnergy = voxelReferences.ToDictionary(
            static reference => reference.Voxel,
            static reference => reference.Voxel.DepositedEnergy);

        // The explicit legacy flag is the only remaining route to the historical
        // full-energy point deposit. Centering on the first intersected voxel keeps
        // comparison inputs stable while reproducing its multi-voxel over-allocation.
        Vector3 legacyHitPoint = intersections[0].Voxel.Center;
        request.TargetPhysiology.ProcessLegacyImpact(
            direction,
            incomingEnergy,
            legacyHitPoint,
            DamageModelVersion.LegacyV1);

        var affected = voxelReferences
            .Select(reference => new
            {
                Reference = reference,
                Delta = reference.Voxel.DepositedEnergy - beforeEnergy[reference.Voxel],
                Projection = Vector3.Dot(reference.Voxel.Center - request.ProjectileState.Position, direction)
            })
            .Where(static item => item.Delta > 0f)
            .OrderBy(static item => item.Projection)
            .ThenBy(static item => item.Reference.StructureId, StringComparer.Ordinal)
            .ToArray();
        if (affected.Length == 0)
            throw new InvalidOperationException("The legacy point-trauma path did not deposit energy.");

        var segments = new List<WoundTrackSegment>(affected.Length);
        var deposits = new List<EnergyDeposit>(affected.Length);
        for (int sequence = 0; sequence < affected.Length; sequence++)
        {
            var item = affected[sequence];
            Vector3 point = item.Reference.Voxel.Center;
            Energy deposit = Energy.FromJoules(item.Delta);
            var stateChange = new ProjectileStateChange(
                sequence,
                ProjectileStateChangeKind.Unchanged,
                point,
                direction,
                direction,
                incomingEnergy,
                incomingEnergy);
            segments.Add(new WoundTrackSegment(
                sequence,
                item.Reference.StructureId,
                item.Reference.BodyRegion,
                item.Reference.StructureType,
                point,
                point,
                Distance.FromMeters(0f),
                incomingEnergy,
                deposit,
                incomingEnergy,
                stateChange));
            deposits.Add(new EnergyDeposit(sequence, item.Reference.StructureId, deposit));
        }

        var ledger = new EnergyLedger(
            incomingEnergy,
            incomingEnergy,
            deposits,
            Energy.FromJoules(0f),
            Energy.FromJoules(0f));
        Vector3 terminalPoint = segments[^1].EndPoint;
        var woundTrack = new WoundTrack(
            request.ImpactId,
            DamageModelVersion.LegacyV1,
            WoundTrackCoordinateSpace.BodyLocalMeters,
            segments[0].EntryPoint,
            ProjectileDisposition.Exited,
            terminalPoint,
            retainedPoint: null,
            segments,
            Array.Empty<FragmentTrack>(),
            ledger);
        var finalState = request.ProjectileState;
        finalState.Position = terminalPoint;

        var warnings = new List<string>
        {
            "legacy-v1 point trauma is non-authoritative and retained only for model comparison."
        };
        if (ledger.ConservationWarning is not null)
            warnings.Add(ledger.ConservationWarning);

        return BuildResult(
            request,
            finalState,
            woundTrack,
            physiologyBefore,
            capabilityBefore,
            randomStreams,
            Array.Empty<CavitationDebugSnapshot>(),
            warnings);
    }

    private ProjectileInteractionResult BuildResult(
        ProjectileInteractionRequest request,
        ProjectileState finalState,
        WoundTrack woundTrack,
        PhysiologyDebugSnapshot physiologyBefore,
        CapabilityDebugSnapshot capabilityBefore,
        IDeterministicRandomStreamProvider randomStreams,
        IEnumerable<CavitationDebugSnapshot> cavitationEffects,
        IEnumerable<string> numericalWarnings)
    {
        IReadOnlyList<Lesion> generatedLesions = Array.Empty<Lesion>();
        if (woundTrack.ModelVersion != DamageModelVersion.LegacyV1 && request.TargetPhysiology is IAnatomicalInjuryTarget target)
        {
            generatedLesions = _lesionGenerator.Generate(woundTrack, target.Anatomy);
            target.LesionRepository.AddRange(generatedLesions);
        }
        PhysiologyDebugSnapshot physiologyAfter = PhysiologyDebugSnapshot.Capture(request.TargetPhysiology);
        CapabilityDebugSnapshot capabilityAfter = CapabilityDebugSnapshot.Capture(request.TargetPhysiology);
        var trace = new ImpactDebugTrace(
            request.ImpactId,
            request.ProjectileProfileId,
            woundTrack.ModelVersion,
            request.ShooterId,
            request.TargetId,
            woundTrack,
            physiologyBefore,
            physiologyAfter,
            capabilityBefore,
            capabilityAfter,
            randomStreams.CaptureSnapshot(),
            generatedLesions: generatedLesions.Select(lesion => JsonSerializer.Serialize<Lesion>(lesion, DamageModelJson.CreateOptions())),
            bleedingSources: CaptureBleedingSources(request.TargetPhysiology.RootBodyPart),
            bloodDestinations: Array.Empty<string>(),
            activeTreatments: CaptureActiveTreatments(request.TargetPhysiology),
            numericalWarnings: numericalWarnings);
        return new ProjectileInteractionResult(finalState, woundTrack, trace, cavitationEffects);
    }

    private IDeterministicRandomStreamProvider GetRandomStreams(ProjectileInteractionRequest request)
    {
        // No uncertainty is enabled in M5, but recording a stable zero-draw stream
        // makes the replay boundary explicit and ready for later seeded variation.
        IDeterministicRandomStreamProvider randomStreams = request.RandomStreams ?? _randomStreams;
        randomStreams.GetStream("damage.projectile-interaction");
        return randomStreams;
    }

    private static Energy KineticEnergy(BallisticProfile profile, Vector3 velocity) =>
        Energy.FromJoules(0.5f * profile.MassKilograms.Kilograms * velocity.LengthSquared());

    private static bool IsEffectivelyStopped(Energy incomingEnergy, Energy outgoingEnergy)
    {
        // The ledger's absolute 0.0001 J accounting tolerance is deliberately not
        // a physical stop threshold: it could retain an otherwise lossless,
        // ultra-low-energy projectile. Normalize only hard float residue or the
        // documented relative share of the incoming energy.
        float relativeResidualJoules = EnergyConservationTolerance.Default.Relative
            * MathF.Abs(incomingEnergy.Joules);
        float stopThresholdJoules = MathF.Max(
            EnergyStopEpsilonJoules,
            relativeResidualJoules);
        return outgoingEnergy.Joules <= stopThresholdJoules;
    }

    private static IReadOnlyList<string> CaptureBleedingSources(BodyPart root)
    {
        var sources = new List<string>();
        AddBleedingSources(root, root.Type.ToString(), sources);
        return sources.AsReadOnly();
    }

    private static void AddBleedingSources(BodyPart part, string path, List<string> sources)
    {
        float rate = part.GetActiveBleedRate();
        if (rate > 0f)
        {
            sources.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{path}:{rate:R} ml/s"));
        }

        for (int index = 0; index < part.Children.Count; index++)
        {
            BodyPart child = part.Children[index];
            AddBleedingSources(child, $"{path}/child-{index:D2}-{child.Type}", sources);
        }
    }

    private static IReadOnlyList<string> CaptureActiveTreatments(IActorPhysiology physiology)
    {
        var treatments = new List<string>();
        if (physiology.HasChestSeal)
            treatments.Add("chest-seal");
        AddActiveTreatments(physiology.RootBodyPart, physiology.RootBodyPart.Type.ToString(), treatments);
        return treatments.AsReadOnly();
    }

    private static void AddActiveTreatments(BodyPart part, string path, List<string> treatments)
    {
        if (part.HasTourniquet)
            treatments.Add($"tourniquet:{path}");
        if (part.HasWoundPacking)
            treatments.Add($"wound-packing:{path}");

        for (int index = 0; index < part.Children.Count; index++)
        {
            BodyPart child = part.Children[index];
            AddActiveTreatments(child, $"{path}/child-{index:D2}-{child.Type}", treatments);
        }
    }
}
