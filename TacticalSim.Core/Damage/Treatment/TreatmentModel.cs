using TacticalSim.Core.Damage.Physiology;
using TacticalSim.Core.Simulation;

namespace TacticalSim.Core.Damage.Treatment;

public enum TreatmentKind { Tourniquet, DirectPressure, WoundPacking, ChestSeal, NeedleDecompression, Assessment }
public enum TreatmentApplicationQuality { Ineffective, Partial, Effective, Perfect }
public enum TreatmentResult { Pending, InProgress, Completed, Partial, Ineffective, Interrupted, Failed }
public enum TreatmentInterruptionPolicy { LoseProgress, PreserveProgress, ApplyPartialEffect }
public enum TreatmentInterruptionReason { ExplicitCancellation, ProviderMovement, Suppression, Incapacitation }
public enum TreatmentPosture { Any, KneelingOrProne, Stationary }
public enum LimbPlacementZone { Proximal, MidLimb, Distal }

public sealed record TreatmentTarget(Guid ActorId, string? LesionId = null, string? BodyRegion = null);
public sealed record TreatmentRequirements(IReadOnlyDictionary<string, int> Equipment, int RequiredHands, TreatmentPosture Posture);
public sealed record TreatmentTraceEntry(float Time, Guid ProviderId, Guid TargetId, TreatmentKind Kind,
    TreatmentResult Result, TreatmentApplicationQuality Quality, string Detail, bool IsDebug);
public sealed record TreatmentReassessment(Guid TargetId, string? LesionId, float DueTime, string Reason);

/// <summary>Finite actor or team medical inventory. Reservations make concurrent actions deterministic.</summary>
public sealed class TreatmentInventory
{
    private readonly Dictionary<string, int> _available = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, IReadOnlyDictionary<string, int>> _reservations = [];
    public TreatmentInventory(IEnumerable<KeyValuePair<string, int>>? loadout = null, bool ignoreInventory = false)
    {
        IgnoreInventory = ignoreInventory;
        foreach (var item in loadout ?? []) Add(item.Key, item.Value);
    }
    public bool IgnoreInventory { get; }
    public IReadOnlyDictionary<string, int> Available => _available;
    public void Add(string item, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _available[item] = GetCount(item) + count;
    }
    public int GetCount(string item) => _available.GetValueOrDefault(item);
    public bool TryReserve(Guid actionId, IReadOnlyDictionary<string, int> required)
    {
        if (_reservations.ContainsKey(actionId)) return true;
        if (!IgnoreInventory && required.Any(x => x.Value < 0 || GetCount(x.Key) < x.Value)) return false;
        if (!IgnoreInventory) foreach (var item in required) _available[item.Key] = GetCount(item.Key) - item.Value;
        _reservations[actionId] = new Dictionary<string, int>(required, StringComparer.Ordinal); return true;
    }
    public void Release(Guid actionId)
    {
        if (!_reservations.Remove(actionId, out var items) || IgnoreInventory) return;
        foreach (var item in items) _available[item.Key] = GetCount(item.Key) + item.Value;
    }
    public void Consume(Guid actionId) => _reservations.Remove(actionId);
}

/// <summary>A timed medical intervention executed by the authoritative turn resolver.</summary>
public sealed class TreatmentAction : TacticalAction
{
    private readonly TreatmentInventory _inventory;
    private readonly Func<TreatmentAction, TreatmentResult> _complete;
    private readonly Action<TreatmentAction, float>? _progress;
    private readonly Action<TreatmentAction>? _interrupt;
    private bool _reserved;

    public TreatmentAction(Guid providerId, TreatmentTarget target, TreatmentKind kind, float duration,
        TreatmentRequirements requirements, TreatmentInventory inventory,
        Func<TreatmentAction, TreatmentResult> complete,
        TreatmentApplicationQuality quality = TreatmentApplicationQuality.Effective,
        TreatmentInterruptionPolicy interruptionPolicy = TreatmentInterruptionPolicy.ApplyPartialEffect,
        bool requiresReassessment = true, float reassessmentDelay = 30f,
        Action<TreatmentAction, float>? progress = null, Action<TreatmentAction>? interrupt = null)
        : base(providerId, duration)
    {
        if (providerId == Guid.Empty) throw new ArgumentException("Provider is required.", nameof(providerId));
        if (target.ActorId == Guid.Empty) throw new ArgumentException("Target actor is required.", nameof(target));
        if (!float.IsFinite(duration) || duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration));
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.RequiredHands is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(requirements));
        if (!float.IsFinite(reassessmentDelay) || reassessmentDelay < 0f) throw new ArgumentOutOfRangeException(nameof(reassessmentDelay));
        Target = target; Kind = kind; Requirements = requirements;
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory)); _complete = complete ?? throw new ArgumentNullException(nameof(complete));
        Quality = quality; InterruptionPolicy = interruptionPolicy; RequiresReassessment = requiresReassessment;
        ReassessmentDelay = reassessmentDelay; _progress = progress; _interrupt = interrupt;
    }
    public Guid ProviderId => ActorId;
    public TreatmentTarget Target { get; }
    public TreatmentKind Kind { get; }
    public TreatmentRequirements Requirements { get; }
    public TreatmentApplicationQuality Quality { get; }
    public TreatmentInterruptionPolicy InterruptionPolicy { get; }
    public TreatmentResult Result { get; private set; } = TreatmentResult.Pending;
    public bool RequiresReassessment { get; }
    public float ReassessmentDelay { get; }
    public TreatmentReassessment? Reassessment { get; private set; }
    public TreatmentInterruptionReason? InterruptionReason { get; private set; }
    public override void Execute(float dt)
    {
        if (!_reserved)
        {
            if (!_inventory.TryReserve(Id, Requirements.Equipment)) throw new InvalidOperationException("Required treatment equipment is unavailable.");
            _reserved = true; Result = TreatmentResult.InProgress;
        }
        _progress?.Invoke(this, dt);
    }
    public override void OnComplete()
    {
        Result = _complete(this); _inventory.Consume(Id);
        if (RequiresReassessment && CompletionTime is float completed)
            Reassessment = new(Target.ActorId, Target.LesionId, completed + ReassessmentDelay, $"Reassess {Kind} result: {Result}.");
    }
    public void Interrupt(TreatmentInterruptionReason reason)
    {
        if (State is TacticalActionState.Completed or TacticalActionState.Cancelled or TacticalActionState.Failed) return;
        InterruptionReason = reason;
    }
    public override void OnCancel()
    {
        InterruptionReason ??= TreatmentInterruptionReason.ExplicitCancellation;
        Result = TreatmentResult.Interrupted; _interrupt?.Invoke(this);
        if (_reserved) _inventory.Release(Id);
        if (InterruptionPolicy == TreatmentInterruptionPolicy.LoseProgress) ExecutionProgress = 0f;
    }
    public override void OnFail(Exception ex) { Result = TreatmentResult.Failed; if (_reserved) _inventory.Release(Id); }
}

public sealed class TreatmentService
{
    private readonly List<TreatmentTraceEntry> _trace = [];
    public IReadOnlyList<TreatmentTraceEntry> Trace => _trace;

    public TreatmentAction CreateTourniquet(Guid provider, TreatmentTarget target, string limb, LimbPlacementZone zone,
        HemorrhagePhysiologyModel physiology, TreatmentInventory inventory, float duration = 8f,
        TreatmentApplicationQuality quality = TreatmentApplicationQuality.Effective, bool secondDevice = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(limb);
        if (string.IsNullOrWhiteSpace(target.LesionId)) throw new ArgumentException("A tourniquet requires a target lesion.", nameof(target));
        var equipment = new Dictionary<string, int> { ["tourniquet"] = 1 };
        return new(provider, target, TreatmentKind.Tourniquet, duration, new(equipment, 2, TreatmentPosture.Stationary), inventory, action =>
        {
            var effectiveQuality = secondDevice && quality == TreatmentApplicationQuality.Partial ? TreatmentApplicationQuality.Effective : quality;
            bool controlled = effectiveQuality != TreatmentApplicationQuality.Ineffective && physiology.TryControlSource(target.LesionId!, BleedingControlState.Tourniquet);
            var result = !controlled ? TreatmentResult.Ineffective : effectiveQuality == TreatmentApplicationQuality.Partial ? TreatmentResult.Partial : TreatmentResult.Completed;
            Record(action, result, $"{limb}/{zone}; second device={secondDevice}"); return result;
        }, quality);
    }

    public TreatmentAction CreatePressureOrPacking(Guid provider, TreatmentTarget target, HemorrhagePhysiologyModel physiology,
        TreatmentInventory inventory, bool packing, float duration = 10f, TreatmentApplicationQuality quality = TreatmentApplicationQuality.Effective)
    {
        if (string.IsNullOrWhiteSpace(target.LesionId)) throw new ArgumentException("A target lesion is required.", nameof(target));
        string lesion = target.LesionId;
        var source = physiology.Sources.FirstOrDefault(x => x.LesionId == lesion);
        if (source is null || !source.Compressible || source.Destination is not (BloodDestination.External or BloodDestination.LocalSoftTissue))
            throw new InvalidOperationException("The wound is not accessible and compressible.");
        var equipment = packing ? new Dictionary<string, int> { ["gauze"] = 1 } : new Dictionary<string, int>();
        return new(provider, target, packing ? TreatmentKind.WoundPacking : TreatmentKind.DirectPressure, duration,
            new(equipment, 2, TreatmentPosture.Stationary), inventory, action =>
            {
                bool controlled = quality != TreatmentApplicationQuality.Ineffective && physiology.TryControlSource(lesion,
                    packing ? BleedingControlState.Packed : BleedingControlState.Compressed);
                var result = !controlled ? TreatmentResult.Ineffective : quality == TreatmentApplicationQuality.Partial ? TreatmentResult.Partial : TreatmentResult.Completed;
                Record(action, result, packing ? "wound packed" : "sustained direct pressure"); return result;
            }, quality,
            progress: packing || quality == TreatmentApplicationQuality.Ineffective
                ? null
                : (_, _) => physiology.TryControlSource(lesion, BleedingControlState.Compressed),
            interrupt: action => physiology.TryControlSource(lesion, BleedingControlState.Uncontrolled));
    }

    public TreatmentResult QuickApply(Guid provider, TreatmentTarget target, TreatmentKind kind,
        Func<TreatmentApplicationQuality, TreatmentResult> apply, bool debugEnabled,
        TreatmentApplicationQuality quality = TreatmentApplicationQuality.Perfect)
    {
        if (!debugEnabled) throw new InvalidOperationException("Quick treatment is disabled outside explicitly enabled debug mode.");
        TreatmentResult result = apply(quality);
        _trace.Add(new(0f, provider, target.ActorId, kind, result, quality, "developer quick treatment", true)); return result;
    }

    public TreatmentTraceEntry Reassess(TreatmentAction action, float time, string observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observation);
        var entry = new TreatmentTraceEntry(time, action.ProviderId, action.Target.ActorId, TreatmentKind.Assessment,
            action.Result, action.Quality, observation, false); _trace.Add(entry); return entry;
    }
    private void Record(TreatmentAction action, TreatmentResult result, string detail) =>
        _trace.Add(new(action.CompletionTime ?? 0f, action.ProviderId, action.Target.ActorId, action.Kind, result, action.Quality, detail, false));
}
