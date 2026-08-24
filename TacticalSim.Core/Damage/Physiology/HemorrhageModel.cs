using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;
using TacticalSim.Core.Physiology;

namespace TacticalSim.Core.Damage.Physiology;

public enum BloodDestination { External, LocalSoftTissue, LeftPleural, RightPleural, Pericardial, Peritoneal, Retroperitoneal, Airway }
public enum BleedingControlState { Uncontrolled, Compressed, Packed, Tourniquet, Definitive }
public enum ClotState { None, Forming, Stable, Disrupted }
public enum CasualtyState { Effective, Incapacitated, Unconscious, Dead }
public enum TacticalCapability { Movement, Posture, Aiming, Firing, Reloading, Communication, SelfAid }

/// <summary>A lesion-owned bleeding source. Rates are deterministic and pressure-dependent.</summary>
public sealed class BleedingSource
{
    public BleedingSource(string lesionId, PressureRegime pressureRegime, float apertureMillimeters,
        bool completeTransection, BloodDestination destination, bool compressible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lesionId);
        if (!float.IsFinite(apertureMillimeters) || apertureMillimeters < 0f)
            throw new ArgumentOutOfRangeException(nameof(apertureMillimeters));
        LesionId = lesionId; PressureRegime = pressureRegime; ApertureMillimeters = apertureMillimeters;
        CompleteTransection = completeTransection; Destination = destination; Compressible = compressible;
    }
    public string LesionId { get; }
    public PressureRegime PressureRegime { get; }
    public float ApertureMillimeters { get; }
    public bool CompleteTransection { get; }
    public BloodDestination Destination { get; }
    public bool Compressible { get; }
    public BleedingControlState ControlState { get; private set; }
    public ClotState ClotState { get; private set; }
    public float ClotProgress { get; private set; }

    public bool TrySetControl(BleedingControlState control)
    {
        if (control is BleedingControlState.Compressed or BleedingControlState.Packed or BleedingControlState.Tourniquet && !Compressible)
            return false;
        ControlState = control;
        return true;
    }

    public void DisruptClot()
    {
        if (ClotState is ClotState.Forming or ClotState.Stable) { ClotState = ClotState.Disrupted; ClotProgress = 0f; }
    }

    internal float CalculateFlow(float mapMmhg)
    {
        if (ControlState == BleedingControlState.Definitive || ApertureMillimeters == 0f || mapMmhg <= 0f) return 0f;
        float sourcePressure = PressureRegime switch { PressureRegime.Arterial => mapMmhg, PressureRegime.Pulmonary => mapMmhg * .25f, PressureRegime.Venous => 12f, _ => mapMmhg * .18f };
        float apertureArea = MathF.PI * MathF.Pow(ApertureMillimeters * .5f, 2f);
        float coefficient = PressureRegime switch { PressureRegime.Arterial => .018f, PressureRegime.Venous => .012f, PressureRegime.Pulmonary => .014f, _ => .006f };
        float transection = CompleteTransection ? 1.15f : 1f;
        float control = ControlState switch { BleedingControlState.Compressed => .25f, BleedingControlState.Packed => .12f, BleedingControlState.Tourniquet => .01f, _ => 1f };
        float clot = ClotState == ClotState.Stable ? .25f : 1f - .5f * ClotProgress;
        return MathF.Max(0f, coefficient * apertureArea * MathF.Sqrt(sourcePressure) * transection * control * clot);
    }

    internal void AdvanceHemostasis(float seconds, float flowMlPerSecond)
    {
        if (seconds <= 0f || ClotState == ClotState.Stable || ControlState == BleedingControlState.Definitive) return;
        // Major/high-flow sources cannot spontaneously seal in this reduced model.
        if (ApertureMillimeters >= 5f || flowMlPerSecond >= 3f) return;
        ClotState = ClotState.Forming;
        float assistance = ControlState == BleedingControlState.Uncontrolled ? 1f : 2f;
        ClotProgress = Math.Clamp(ClotProgress + seconds / 300f * assistance, 0f, 1f);
        if (ClotProgress >= 1f) ClotState = ClotState.Stable;
    }
}

public sealed class BloodCompartmentLedger
{
    private readonly Dictionary<BloodDestination, float> _lost = Enum.GetValues<BloodDestination>().ToDictionary(x => x, _ => 0f);
    public BloodCompartmentLedger(float baselineMilliliters = 5000f)
    {
        if (!float.IsFinite(baselineMilliliters) || baselineMilliliters <= 0f) throw new ArgumentOutOfRangeException(nameof(baselineMilliliters));
        BaselineMilliliters = CirculatingMilliliters = baselineMilliliters;
    }
    public float BaselineMilliliters { get; }
    public float CirculatingMilliliters { get; private set; }
    public IReadOnlyDictionary<BloodDestination, float> LostByDestination => _lost;
    public float TotalLostMilliliters => _lost.Values.Sum();
    public float ConservationErrorMilliliters => BaselineMilliliters - CirculatingMilliliters - TotalLostMilliliters;
    internal float Lose(BloodDestination destination, float requestedMl)
    {
        float actual = Math.Clamp(requestedMl, 0f, CirculatingMilliliters);
        CirculatingMilliliters -= actual; _lost[destination] += actual; return actual;
    }
}

public sealed record CardiovascularState(float HeartRateBpm, float StrokeVolumeIndex, float CardiacOutputIndex,
    float SystemicVascularResistanceIndex, float MeanArterialPressureMmhg, float PerfusionEffectiveness);
public sealed record OxygenDeliveryState(float VentilationEffectiveness, float ArterialSaturation,
    float RedCellMassFraction, float SystemicDeliveryIndex, float CerebralDeliveryIndex);
public sealed record CapabilityState(IReadOnlyDictionary<TacticalCapability, float> Capacity, IReadOnlyList<string> Reasons)
{
    public float this[TacticalCapability capability] => Capacity[capability];
    public bool IsAvailable(TacticalCapability capability) => this[capability] > 0f;
}

public sealed class PhysiologyCapabilityResolver
{
    public CapabilityState Resolve(CardiovascularState cardiovascular, OxygenDeliveryState oxygen, CasualtyState casualty,
        MusculoskeletalFunctionalState musculoskeletal, NeurologicalFunctionalState neurological)
    {
        float systemic = MathF.Min(cardiovascular.PerfusionEffectiveness, oxygen.SystemicDeliveryIndex);
        float cerebral = oxygen.CerebralDeliveryIndex;
        float awake = casualty >= CasualtyState.Unconscious ? 0f : 1f;
        float cognitive = neurological.CognitiveCapacity;
        var values = new Dictionary<TacticalCapability, float>
        {
            [TacticalCapability.Movement] = awake * MathF.Min(systemic, MathF.Min(musculoskeletal.MovementCapacity, neurological.LowerLimbCapacity)),
            [TacticalCapability.Posture] = awake * MathF.Min(systemic, musculoskeletal.StandingCapacity),
            [TacticalCapability.Aiming] = awake * MathF.Min(cognitive, MathF.Min(cerebral, neurological.UpperLimbCapacity)),
            [TacticalCapability.Firing] = awake * MathF.Min(cognitive, MathF.Min(cerebral, MathF.Min(musculoskeletal.UpperLimbCapacity, neurological.UpperLimbCapacity))),
            [TacticalCapability.Reloading] = awake * MathF.Min(cognitive, MathF.Min(systemic, MathF.Min(musculoskeletal.UpperLimbCapacity, neurological.UpperLimbCapacity))),
            [TacticalCapability.Communication] = awake * MathF.Min(cognitive, cerebral),
            [TacticalCapability.SelfAid] = awake * MathF.Min(cognitive, MathF.Min(systemic, MathF.Min(cerebral, neurological.UpperLimbCapacity)))
        };
        var reasons = new List<string>();
        if (cardiovascular.PerfusionEffectiveness < .8f) reasons.Add("reduced perfusion");
        if (oxygen.SystemicDeliveryIndex < .8f) reasons.Add("reduced oxygen delivery");
        if (musculoskeletal.MovementCapacity < 1f) reasons.Add("musculoskeletal injury");
        if (neurological.UpperLimbCapacity < 1f || neurological.LowerLimbCapacity < 1f) reasons.Add("neurological injury");
        if (neurological.CognitiveCapacity < 1f) reasons.Add("brain injury");
        if (awake == 0f) reasons.Add(casualty.ToString().ToLowerInvariant());
        return new(values, reasons);
    }
}

/// <summary>Authoritative M7 lesion-to-capability progression model.</summary>
public sealed class HemorrhagePhysiologyModel
{
    private readonly List<BleedingSource> _sources = [];
    private float _lowCerebralDeliverySeconds;
    public HemorrhagePhysiologyModel(float baselineBloodMl = 5000f) { Blood = new(baselineBloodMl); Recalculate(); }
    public BloodCompartmentLedger Blood { get; }
    public IReadOnlyList<BleedingSource> Sources => _sources;
    public CardiovascularState Cardiovascular { get; private set; } = null!;
    public OxygenDeliveryState OxygenDelivery { get; private set; } = null!;
    public CasualtyState CasualtyState { get; private set; }
    public float LowCerebralDeliverySeconds => _lowCerebralDeliverySeconds;
    public float VentilationEffectiveness { get; set; } = 1f;
    public float CardiacFunction { get; set; } = 1f;
    public float CurrentBleedRateMlPerSecond => _sources.Sum(x => x.CalculateFlow(Cardiovascular.MeanArterialPressureMmhg));
    public void AddSource(BleedingSource source) { ArgumentNullException.ThrowIfNull(source); if (_sources.Any(x => x.LesionId == source.LesionId)) throw new InvalidOperationException($"A source for lesion '{source.LesionId}' already exists."); _sources.Add(source); _sources.Sort((a,b)=>StringComparer.Ordinal.Compare(a.LesionId,b.LesionId)); }
    public bool TryControlSource(string lesionId, BleedingControlState control) => _sources.FirstOrDefault(x => x.LesionId == lesionId)?.TrySetControl(control) ?? false;
    public void ApplyMovementStress(float intensity) { if (intensity <= 0f) return; foreach (var source in _sources.Where(x => x.ClotState == ClotState.Stable && intensity >= .5f)) source.DisruptClot(); }
    public void Tick(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
        // Fixed internal steps keep large and small caller timesteps close and prevent pressure overshoot.
        float remaining = seconds;
        while (remaining > 0f) { float step = MathF.Min(.1f, remaining); TickStep(step); remaining -= step; }
    }
    private void TickStep(float dt)
    {
        foreach (BleedingSource source in _sources)
        {
            float flow = source.CalculateFlow(Cardiovascular.MeanArterialPressureMmhg);
            Blood.Lose(source.Destination, flow * dt); source.AdvanceHemostasis(dt, flow);
        }
        Recalculate();
        if (OxygenDelivery.CerebralDeliveryIndex < .18f) _lowCerebralDeliverySeconds += dt; else _lowCerebralDeliverySeconds = MathF.Max(0f, _lowCerebralDeliverySeconds - dt * .5f);
        if (CasualtyState != CasualtyState.Dead)
            CasualtyState = _lowCerebralDeliverySeconds >= 30f || Cardiovascular.CardiacOutputIndex <= .01f ? CasualtyState.Dead
                : OxygenDelivery.CerebralDeliveryIndex < .25f ? CasualtyState.Unconscious
                : MathF.Min(Cardiovascular.PerfusionEffectiveness, OxygenDelivery.SystemicDeliveryIndex) < .55f ? CasualtyState.Incapacitated : CasualtyState.Effective;
    }
    private void Recalculate()
    {
        float volume = Math.Clamp(Blood.CirculatingMilliliters / Blood.BaselineMilliliters, 0f, 1f);
        float compensation = Math.Clamp((1f - volume) * 2.2f, 0f, .65f);
        float heartRate = 80f * (1f + compensation) * Math.Clamp(CardiacFunction, 0f, 1f);
        float stroke = Math.Clamp((.15f + .85f * volume) * CardiacFunction, 0f, 1f);
        float output = Math.Clamp((heartRate / 80f) * stroke, 0f, 1.25f);
        float resistance = 1f + Math.Clamp((1f - volume) * 1.5f, 0f, .8f);
        float map = Math.Clamp(93f * output * resistance, 0f, 120f);
        float perfusion = Math.Clamp(map / 70f, 0f, 1f);
        Cardiovascular = new(heartRate, stroke, output, resistance, map, perfusion);
        float saturation = Math.Clamp(.72f + .28f * VentilationEffectiveness, 0f, 1f);
        float systemic = Math.Clamp(saturation * volume * output, 0f, 1f);
        OxygenDelivery = new(Math.Clamp(VentilationEffectiveness, 0f, 1f), saturation, volume, systemic, Math.Clamp(systemic * perfusion, 0f, 1f));
    }
}

public static class BleedingSourceFactory
{
    public static BleedingSource? FromLesion(Lesion lesion, IAnatomicalStructureCatalog anatomy)
    {
        ArgumentNullException.ThrowIfNull(lesion); ArgumentNullException.ThrowIfNull(anatomy);
        AnatomicalStructure structure;
        try { structure = anatomy.GetRequired(lesion.StructureId); } catch (KeyNotFoundException) { return null; }
        PressureRegime regime; float aperture; bool transection = false;
        if (lesion is VesselLesion vessel) { regime = vessel.PressureRegime; aperture = vessel.Aperture.Meters * 1000f; transection = vessel.CompleteTransection; }
        else if (lesion.Kind is LesionKind.ParenchymalInjury or LesionKind.CardiacInjury
                 or LesionKind.OpenSoftTissueWound
                 || lesion.Kind == LesionKind.BrainOrSpinalInjury
                    && structure.Region == BodyPartType.Head
                    && structure.Type == AnatomicalStructureType.Organ)
        { regime = lesion.Kind == LesionKind.CardiacInjury ? PressureRegime.Arterial : PressureRegime.Parenchymal; aperture = 1f + lesion.Severity * 3f; }
        else return null;
        bool compressible = structure.Region is BodyPartType.LeftArm or BodyPartType.RightArm or BodyPartType.LeftLeg or BodyPartType.RightLeg || structure.Type is AnatomicalStructureType.Skin;
        return new(lesion.Id, regime, aperture, transection, DestinationFor(structure), compressible);
    }
    private static BloodDestination DestinationFor(AnatomicalStructure s) => s.Region switch
    {
        BodyPartType.Thorax when s.Type == AnatomicalStructureType.Pericardium || s.FunctionalRole == FunctionalRole.Cardiac => BloodDestination.Pericardial,
        BodyPartType.Thorax when s.Laterality == "left" => BloodDestination.LeftPleural,
        BodyPartType.Thorax when s.Laterality == "right" => BloodDestination.RightPleural,
        BodyPartType.Abdomen when s.Id.Contains("kidney", StringComparison.Ordinal) => BloodDestination.Retroperitoneal,
        BodyPartType.Abdomen => BloodDestination.Peritoneal,
        BodyPartType.Neck when s.Type == AnatomicalStructureType.Airway => BloodDestination.Airway,
        _ when s.Type is AnatomicalStructureType.Skin => BloodDestination.External,
        _ => BloodDestination.LocalSoftTissue
    };
}
