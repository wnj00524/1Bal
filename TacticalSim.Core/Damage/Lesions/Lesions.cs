using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.Json.Serialization;
using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Ballistics;
using TacticalSim.Core.Physiology;
using TacticalSim.Core.Units;

namespace TacticalSim.Core.Damage.Lesions;

public enum LesionKind { VesselLaceration, VesselTransection, ParenchymalInjury, Fracture, NerveInjury, AirwayDisruption, PleuralBreach, CardiacInjury, BrainOrSpinalInjury, OpenSoftTissueWound }
public enum LesionTreatmentState { Untreated, TemporarilyControlled, DefinitivelyTreated }
public enum FractureStability { Stable, Displaced, Unstable }
public enum FractureFunctionalConsequence { LimitedUse, SevereRestriction, StructuralFunctionLost }
public enum NerveDamageGrade { Neuropraxia, PartialDisruption, CompleteDisruption }

/// <summary>
/// Classifies generated fractures using the existing M6 severity boundaries.
/// Both comparisons are intentionally strict: 0.30 remains stable and 0.65
/// remains displaced. The thresholds are provisional gameplay calibration.
/// </summary>
public static class FractureStabilityClassifier
{
    public const float DisplacedSeverityThreshold = 0.30f;
    public const float UnstableSeverityThreshold = 0.65f;

    public static FractureStability Classify(float severity)
    {
        if (!float.IsFinite(severity) || severity is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(severity));

        return severity > UnstableSeverityThreshold
            ? FractureStability.Unstable
            : severity > DisplacedSeverityThreshold
                ? FractureStability.Displaced
                : FractureStability.Stable;
    }
}

public sealed record LesionGeometry(Vector3 Center, Vector3 TrackDirection, Distance Length, Distance Radius);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$lesionType")]
[JsonDerivedType(typeof(VesselLesion), "vessel")]
[JsonDerivedType(typeof(FractureLesion), "fracture")]
[JsonDerivedType(typeof(NerveLesion), "nerve")]
[JsonDerivedType(typeof(TissueLesion), "tissue")]
public abstract record Lesion
{
    protected Lesion(string id, string structureId, string originImpactId, LesionKind kind, float severity,
        LesionGeometry geometry, LesionTreatmentState treatmentState, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id); ArgumentException.ThrowIfNullOrWhiteSpace(structureId); ArgumentException.ThrowIfNullOrWhiteSpace(originImpactId);
        if (!float.IsFinite(severity) || severity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(severity));
        Id=id; StructureId=structureId; OriginImpactId=originImpactId; Kind=kind; Severity=severity; Geometry=geometry ?? throw new ArgumentNullException(nameof(geometry)); TreatmentState=treatmentState; CreatedAt=createdAt;
    }
    public string Id { get; init; }
    public string StructureId { get; init; }
    public string OriginImpactId { get; init; }
    public LesionKind Kind { get; init; }
    public float Severity { get; init; }
    public LesionGeometry Geometry { get; init; }
    public LesionTreatmentState TreatmentState { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record VesselLesion : Lesion
{
    [JsonConstructor] public VesselLesion(string id,string structureId,string originImpactId,LesionKind kind,float severity,LesionGeometry geometry,LesionTreatmentState treatmentState,DateTimeOffset createdAt,Distance aperture,PressureRegime pressureRegime,bool completeTransection)
        : base(id,structureId,originImpactId,kind,severity,geometry,treatmentState,createdAt) { if (kind is not (LesionKind.VesselLaceration or LesionKind.VesselTransection)) throw new ArgumentException("Invalid vessel lesion kind.",nameof(kind)); Aperture=aperture; PressureRegime=pressureRegime; CompleteTransection=completeTransection; }
    public Distance Aperture { get; init; }
    public PressureRegime PressureRegime { get; init; }
    public bool CompleteTransection { get; init; }
}
public sealed record FractureLesion : Lesion
{
    [JsonConstructor] public FractureLesion(string id,string structureId,string originImpactId,float severity,LesionGeometry geometry,LesionTreatmentState treatmentState,DateTimeOffset createdAt,FractureStability stability,bool weightBearing)
        : base(id,structureId,originImpactId,LesionKind.Fracture,severity,geometry,treatmentState,createdAt) { Stability=stability; WeightBearing=weightBearing; }
    public FractureStability Stability { get; init; }
    public bool WeightBearing { get; init; }

    /// <summary>
    /// Deterministic functional interpretation of the persisted stability class.
    /// This is computed rather than serialized so the fracture JSON contract and
    /// its existing constructor remain unchanged.
    /// </summary>
    [JsonIgnore]
    public FractureFunctionalConsequence FunctionalConsequence => Stability switch
    {
        FractureStability.Stable => FractureFunctionalConsequence.LimitedUse,
        FractureStability.Displaced => FractureFunctionalConsequence.SevereRestriction,
        FractureStability.Unstable => FractureFunctionalConsequence.StructuralFunctionLost,
        _ => throw new InvalidOperationException($"Unknown fracture stability '{Stability}'.")
    };
}
public sealed record NerveLesion : Lesion
{
    [JsonConstructor] public NerveLesion(string id,string structureId,string originImpactId,LesionKind kind,float severity,LesionGeometry geometry,LesionTreatmentState treatmentState,DateTimeOffset createdAt,NerveDamageGrade grade,string? laterality,string? neurologicalLevel)
        : base(id,structureId,originImpactId,kind,severity,geometry,treatmentState,createdAt) { Grade=grade; Laterality=laterality; NeurologicalLevel=neurologicalLevel; }
    public NerveDamageGrade Grade { get; init; }
    public string? Laterality { get; init; }
    public string? NeurologicalLevel { get; init; }
}
public sealed record TissueLesion : Lesion
{
    [JsonConstructor] public TissueLesion(string id,string structureId,string originImpactId,LesionKind kind,float severity,LesionGeometry geometry,LesionTreatmentState treatmentState,DateTimeOffset createdAt)
        : base(id,structureId,originImpactId,kind,severity,geometry,treatmentState,createdAt) { }
}

public interface ILesionRepository
{
    IReadOnlyList<Lesion> Lesions { get; }
    void AddRange(IEnumerable<Lesion> lesions);
    bool TrySetTreatmentState(string lesionId, LesionTreatmentState state);
    bool ContainsImpact(string originImpactId);
}

public sealed class LesionRepository : ILesionRepository
{
    private readonly List<Lesion> _lesions = [];
    public IReadOnlyList<Lesion> Lesions => new ReadOnlyCollection<Lesion>(_lesions.ToArray());
    public void AddRange(IEnumerable<Lesion> lesions)
    {
        ArgumentNullException.ThrowIfNull(lesions);
        foreach (Lesion lesion in lesions.OrderBy(x => x.Id, StringComparer.Ordinal))
        { if (_lesions.Any(x => x.Id == lesion.Id)) throw new InvalidOperationException($"Duplicate lesion ID '{lesion.Id}'."); _lesions.Add(lesion); }
    }
    public bool TrySetTreatmentState(string lesionId, LesionTreatmentState state)
    { int index=_lesions.FindIndex(x=>x.Id==lesionId); if(index<0)return false; _lesions[index]=_lesions[index] with { TreatmentState=state }; return true; }
    public bool ContainsImpact(string originImpactId)
    { ArgumentException.ThrowIfNullOrWhiteSpace(originImpactId); return _lesions.Any(x => x.OriginImpactId == originImpactId); }
}

public interface IAnatomicalInjuryTarget
{
    IAnatomicalStructureCatalog Anatomy { get; }
    ILesionRepository LesionRepository { get; }
    /// <summary>
    /// Atomically applies all lesions produced by one impact. Implementations own
    /// duplicate-impact handling and creation timestamps because both depend on
    /// authoritative actor state rather than projectile geometry.
    /// </summary>
    bool ApplyImpact(string impactId, IEnumerable<Lesion> lesions);
}

public interface ILesionGenerator { IReadOnlyList<Lesion> Generate(WoundTrack track, IAnatomicalStructureCatalog anatomy); }

/// <summary>Deterministic M6 translation of wound geometry into persistent structural injury.</summary>
public sealed class LesionGenerator : ILesionGenerator
{
    // Provisional M12 calibration: a wound track that physically intersects the
    // brain is not equivalent to a diffuse low-energy deposit in generic tissue.
    // Until regional brain anatomy is available, the deterministic model treats
    // that penetrating structural injury as at least immediately unconscious.
    // The value is registered in IntegratedNeurologicalParameterProvenance.
    public const float PenetratingBrainMinimumSeverity = .30f;

    public IReadOnlyList<Lesion> Generate(WoundTrack track, IAnatomicalStructureCatalog anatomy)
    {
        ArgumentNullException.ThrowIfNull(track); ArgumentNullException.ThrowIfNull(anatomy);
        var candidates = new List<(AnatomicalStructure Structure, WoundTrackSegment Segment, float Severity, float Radius, float EntryDistance)>();
        float traversed = 0f;
        foreach (WoundTrackSegment segment in track.Segments.Where(x=>x.TransferredEnergy.Joules>0))
        {
            float cavityRadius=MathF.Sqrt(MathF.Max(segment.TransferredEnergy.Joules,0))*0.00035f; // provisional gameplay-calibrated M6 mapping
            StructureIntersection[] hits=anatomy.QueryIntersections(segment.EntryPoint,segment.EndPoint,cavityRadius).ToArray();
            foreach (StructureIntersection hit in hits)
            {
                AnatomicalStructure structure = anatomy.GetRequired(hit.StructureId);
                var clippedState = new ProjectileStateChange(segment.Sequence, segment.ProjectileStateChange.Kind, hit.ExitPoint,
                    segment.ProjectileStateChange.IncomingDirection, segment.ProjectileStateChange.OutgoingDirection,
                    segment.IncomingEnergy, segment.OutgoingEnergy);
                var clipped = new WoundTrackSegment(segment.Sequence, structure.Id, segment.BodyRegion, structure.Type.ToString(),
                    hit.EntryPoint, hit.ExitPoint, Distance.FromMeters(MathF.Max(0f, hit.ExitDistance.Meters-hit.EntryDistance.Meters)),
                    segment.IncomingEnergy, segment.TransferredEnergy, segment.OutgoingEnergy, clippedState);
                candidates.Add((structure, clipped, Math.Clamp(segment.TransferredEnergy.Joules/MathF.Max(20f, structure.Calibre.Meters*3000f),.01f,1f), cavityRadius, traversed+hit.EntryDistance.Meters));
            }
            if (hits.Length==0)
            {
                var geometry=new LesionGeometry((segment.EntryPoint+segment.EndPoint)/2,SafeDirection(segment),segment.PathLength,Distance.FromMeters(MathF.Max(.0005f,cavityRadius)));
                candidates.Add((new AnatomicalStructure(segment.StructureId, segment.StructureId, AnatomicalStructureType.Skin, BodyPartType.Thorax, segment.EntryPoint, segment.EndPoint, Distance.FromMeters(MathF.Max(.0005f,cavityRadius))), segment, Math.Clamp(segment.TransferredEnergy.Joules/100f,.01f,1f), cavityRadius, traversed));
            }
            traversed += segment.PathLength.Meters;
        }
        var result = new List<Lesion>(); int ordinal=0;
        foreach (var group in candidates.GroupBy(x => x.Structure.Id, StringComparer.Ordinal)
                     .OrderBy(x => x.Min(y => y.EntryDistance)).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            var first = group.OrderBy(x => x.EntryDistance).First();
            var last = group.OrderBy(x => x.EntryDistance).Last();
            Vector3 start = first.Segment.EntryPoint, end = last.Segment.EndPoint;
            Vector3 center = (start + end) / 2;
            center = new(MathF.Abs(center.X) < 1e-7f ? 0f : center.X, MathF.Abs(center.Y) < 1e-7f ? 0f : center.Y, MathF.Abs(center.Z) < 1e-7f ? 0f : center.Z);
            var geometry = new LesionGeometry(center, SafeDirection(first.Segment),
                Distance.FromMeters(group.Sum(x => x.Segment.PathLength.Meters)), Distance.FromMeters(MathF.Max(.0005f, group.Max(x => x.Radius))));
            string id=$"lesion/{track.TrackId}/{ordinal++:D4}/{group.Key}";
            float severity = CalibrateStructuralSeverity(
                track.ModelVersion, first.Structure, group.Max(x => x.Severity));
            result.Add(Create(id, track.TrackId, first.Structure, severity, geometry));
        }
        return result.AsReadOnly();
    }

    private static float CalibrateStructuralSeverity(
        DamageModelVersion modelVersion,
        AnatomicalStructure structure,
        float energySeverity) =>
        modelVersion == DamageModelVersion.IntegratedV3
        && string.Equals(structure.Id, "organ.brain", StringComparison.Ordinal)
            ? MathF.Max(energySeverity, PenetratingBrainMinimumSeverity)
            : energySeverity;

    private static Lesion Create(string id,string impact,AnatomicalStructure s,float severity,LesionGeometry g)
    {
        DateTimeOffset created=DateTimeOffset.UnixEpoch;
        if(s.Type is AnatomicalStructureType.Artery or AnatomicalStructureType.Vein)
        { bool transection=g.Radius.Meters*2>=s.Calibre.Meters; return new VesselLesion(id,s.Id,impact,transection?LesionKind.VesselTransection:LesionKind.VesselLaceration,severity,g,LesionTreatmentState.Untreated,created,Distance.FromMeters(MathF.Min(s.Calibre.Meters,g.Radius.Meters*2)),s.PressureRegime,transection); }
        if(s.Type==AnatomicalStructureType.Bone) return new FractureLesion(id,s.Id,impact,severity,g,LesionTreatmentState.Untreated,created,FractureStabilityClassifier.Classify(severity),s.FunctionalRole==FunctionalRole.WeightBearing);
        if(s.Type==AnatomicalStructureType.Nerve) return new NerveLesion(id,s.Id,impact,s.FunctionalRole==FunctionalRole.SpinalCord?LesionKind.BrainOrSpinalInjury:LesionKind.NerveInjury,severity,g,LesionTreatmentState.Untreated,created,severity>.7f?NerveDamageGrade.CompleteDisruption:severity>.3f?NerveDamageGrade.PartialDisruption:NerveDamageGrade.Neuropraxia,s.FunctionalRole==FunctionalRole.SpinalCord?InferSpinalLaterality(g.Center.X):s.Laterality,s.FunctionalRole==FunctionalRole.SpinalCord?InferSpinalLevel(s.Id,g.Center.Y):null);
        LesionKind kind = string.Equals(s.Id, "organ.brain", StringComparison.Ordinal)
            ? LesionKind.BrainOrSpinalInjury
            : s.Type switch
            {
                AnatomicalStructureType.Airway => LesionKind.AirwayDisruption,
                AnatomicalStructureType.Pleura => LesionKind.PleuralBreach,
                AnatomicalStructureType.Pericardium => LesionKind.CardiacInjury,
                AnatomicalStructureType.Organ => LesionKind.ParenchymalInjury,
                _ => LesionKind.OpenSoftTissueWound
            };
        return new TissueLesion(id,s.Id,impact,kind,severity,g,LesionTreatmentState.Untreated,created);
    }
    private static Vector3 SafeDirection(WoundTrackSegment s) { Vector3 d=s.EndPoint-s.EntryPoint; return d.LengthSquared()>0?Vector3.Normalize(d):Vector3.Zero; }
    private static string InferSpinalLevel(string structureId,float y)=>structureId.EndsWith("-cervical",StringComparison.Ordinal)?"cervical":structureId.EndsWith("-thoracic",StringComparison.Ordinal)?"thoracic":structureId.EndsWith("-lumbar",StringComparison.Ordinal)?"lumbar":y>.5f?"cervical":y>.2f?"thoracic":"lumbar";
    private static string? InferSpinalLaterality(float x)=>x < -.001f?"left":x > .001f?"right":null;
}

public sealed record LesionDebugItem(string LesionId,string StructureId,string StructureName,LesionKind Type,float Severity,LesionTreatmentState TreatmentState,string OriginImpactId,string Details)
{
    /// <summary>Lesion center in the anatomy catalog's body-local metre coordinate system.</summary>
    public Vector3 BodyLocalCenter { get; init; }

    /// <summary>Present only for fracture rows.</summary>
    public FractureFunctionalConsequence? FunctionalConsequence { get; init; }
}
public static class LesionDebugInspector
{
    public static IReadOnlyList<LesionDebugItem> Inspect(IAnatomicalInjuryTarget target) => target.LesionRepository.Lesions.Select(l =>
    {
        string name;
        try{name=target.Anatomy.GetRequired(l.StructureId).DisplayName;}catch(KeyNotFoundException){name=l.StructureId;}
        string center=FormattableString.Invariant(
            $"({l.Geometry.Center.X:F4}, {l.Geometry.Center.Y:F4}, {l.Geometry.Center.Z:F4})m body-local");
        string details=l switch
        {
            VesselLesion v=>FormattableString.Invariant($"{v.PressureRegime}; aperture={v.Aperture.Meters:F4}m; transection={v.CompleteTransection}; center={center}"),
            FractureLesion f=>FormattableString.Invariant($"{f.Stability}; consequence={f.FunctionalConsequence}; weightBearing={f.WeightBearing}; center={center}"),
            NerveLesion n=>FormattableString.Invariant($"{n.Grade}; side={n.Laterality??"midline"}; level={n.NeurologicalLevel??"n/a"}; center={center}"),
            _=>$"persistent tissue lesion; center={center}"
        };
        return new LesionDebugItem(l.Id,l.StructureId,name,l.Kind,l.Severity,l.TreatmentState,l.OriginImpactId,details)
        {
            BodyLocalCenter = l.Geometry.Center,
            FunctionalConsequence = (l as FractureLesion)?.FunctionalConsequence
        };
    }).ToArray();
}
