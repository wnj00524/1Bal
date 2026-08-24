using TacticalSim.Core.Damage.Anatomy;
using TacticalSim.Core.Damage.Lesions;

namespace TacticalSim.Core.Damage.Physiology;

public enum ThoracicSide { Left, Right }
public enum ChestSealState { None, Effective, Vented, Partial, Blocked, Detached }
public enum DecompressionOutcome { Successful, Partial, Ineffective, WrongSide }

/// <summary>Configurable, deterministic M8 parameters. Values are provisional gameplay calibration.</summary>
public sealed record ThoracicModelParameters(
    float PleuralComplianceMlPerKpa = 450f,
    float LungCompressionVolumeMl = 1800f,
    float TensionPressureKpa = 2.5f,
    float OpenWoundConductancePerSecond = .08f,
    float NeedleVentingMlPerSecond = 180f,
    float TamponadeOnsetMl = 100f,
    float TamponadeCriticalMl = 300f);

public sealed record ThoracicLesion
{
    public ThoracicLesion(string id, ThoracicSide side, float lungLeakMlPerSecond,
        float openWoundArea, bool oneWayValve, float pulmonaryFunctionLoss = 0f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!float.IsFinite(lungLeakMlPerSecond) || lungLeakMlPerSecond < 0f) throw new ArgumentOutOfRangeException(nameof(lungLeakMlPerSecond));
        if (!float.IsFinite(openWoundArea) || openWoundArea is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(openWoundArea));
        if (!float.IsFinite(pulmonaryFunctionLoss) || pulmonaryFunctionLoss is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(pulmonaryFunctionLoss));
        Id = id; Side = side; LungLeakMlPerSecond = lungLeakMlPerSecond;
        OpenWoundArea = openWoundArea; OneWayValve = oneWayValve; PulmonaryFunctionLoss = pulmonaryFunctionLoss;
    }
    public string Id { get; }
    public ThoracicSide Side { get; }
    public float LungLeakMlPerSecond { get; }
    public float OpenWoundArea { get; }
    public bool OneWayValve { get; }
    public float PulmonaryFunctionLoss { get; }
}

public sealed class PleuralCompartment
{
    internal PleuralCompartment(ThoracicSide side) => Side = side;
    public ThoracicSide Side { get; }
    public float GasMilliliters { get; internal set; }
    public float BloodMilliliters { get; internal set; }
    public float PressureKpa { get; internal set; }
    public float LungCompression { get; internal set; }
    public ChestSealState SealState { get; internal set; }
    public bool NeedleInPlace { get; internal set; }
    public bool IsTension { get; internal set; }
}

public sealed record ThoracicState(
    PleuralCompartment Left,
    PleuralCompartment Right,
    float VentilationEffectiveness,
    float CardiacOutputModifier,
    float PericardialBloodMilliliters,
    float TamponadeSeverity);

/// <summary>
/// Authoritative bilateral thoracic mechanism model. It consumes persistent lesion
/// identity and the M7 conserved blood ledger, then supplies respiratory and cardiac
/// modifiers back to the M7 physiology/capability pipeline.
/// </summary>
public sealed class ThoracicInjuryModel
{
    private readonly HemorrhagePhysiologyModel _physiology;
    private readonly ThoracicModelParameters _parameters;
    private readonly List<ThoracicLesion> _lesions = [];
    public ThoracicInjuryModel(HemorrhagePhysiologyModel physiology, ThoracicModelParameters? parameters = null)
    {
        _physiology = physiology ?? throw new ArgumentNullException(nameof(physiology));
        _parameters = parameters ?? new();
        if (_parameters.PleuralComplianceMlPerKpa <= 0f || _parameters.LungCompressionVolumeMl <= 0f || _parameters.TensionPressureKpa <= 0f)
            throw new ArgumentOutOfRangeException(nameof(parameters));
        Left = new(ThoracicSide.Left); Right = new(ThoracicSide.Right);
        State = new(Left, Right, 1f, 1f, 0f, 0f);
    }
    public PleuralCompartment Left { get; }
    public PleuralCompartment Right { get; }
    public ThoracicState State { get; private set; }
    public IReadOnlyList<ThoracicLesion> Lesions => _lesions;
    public float NeurologicalVentilationModifier { get; set; } = 1f;
    public float NeurologicalCardiacModifier { get; set; } = 1f;

    public void AddLesion(ThoracicLesion lesion)
    {
        ArgumentNullException.ThrowIfNull(lesion);
        if (_lesions.Any(x => x.Id == lesion.Id)) throw new InvalidOperationException($"Duplicate thoracic lesion '{lesion.Id}'.");
        _lesions.Add(lesion); _lesions.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
    }

    public bool AddPleuralLesion(Lesion lesion, IAnatomicalStructureCatalog anatomy, float lungLeakMlPerSecond, float openWoundArea = 0f, bool oneWayValve = false)
    {
        ArgumentNullException.ThrowIfNull(lesion); ArgumentNullException.ThrowIfNull(anatomy);
        if (lesion.Kind is not (LesionKind.PleuralBreach or LesionKind.ParenchymalInjury)) return false;
        AnatomicalStructure structure;
        try { structure = anatomy.GetRequired(lesion.StructureId); } catch (KeyNotFoundException) { return false; }
        ThoracicSide? side = structure.Laterality switch { "left" => ThoracicSide.Left, "right" => ThoracicSide.Right, _ => null };
        if (side is null) return false;
        AddLesion(new(lesion.Id, side.Value, lungLeakMlPerSecond, openWoundArea, oneWayValve, lesion.Kind == LesionKind.ParenchymalInjury ? lesion.Severity : 0f));
        return true;
    }

    public bool ApplyChestSeal(ThoracicSide side, ChestSealState state)
    {
        if (state == ChestSealState.None) return false;
        Compartment(side).SealState = state; return true;
    }

    public DecompressionOutcome NeedleDecompress(ThoracicSide side, float placementQuality = 1f)
    {
        if (!float.IsFinite(placementQuality) || placementQuality is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(placementQuality));
        PleuralCompartment target = Compartment(side);
        PleuralCompartment other = Compartment(side == ThoracicSide.Left ? ThoracicSide.Right : ThoracicSide.Left);
        if (target.PressureKpa <= 0f) return other.IsTension ? DecompressionOutcome.WrongSide : DecompressionOutcome.Ineffective;
        if (placementQuality < .25f) return DecompressionOutcome.Ineffective;
        target.NeedleInPlace = true;
        if (placementQuality < .75f) { target.GasMilliliters *= .75f; Recalculate(target); return DecompressionOutcome.Partial; }
        target.GasMilliliters = MathF.Min(target.GasMilliliters, _parameters.TensionPressureKpa * _parameters.PleuralComplianceMlPerKpa * .4f);
        Recalculate(target);
        return DecompressionOutcome.Successful;
    }

    public void Tick(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
        float remaining = seconds;
        while (remaining > 0f) { float step = MathF.Min(.1f, remaining); TickThorax(step); _physiology.Tick(step); remaining -= step; }
    }

    private void TickThorax(float dt)
    {
        UpdateCompartment(Left, dt); UpdateCompartment(Right, dt);
        Left.BloodMilliliters = _physiology.Blood.LostByDestination[BloodDestination.LeftPleural];
        Right.BloodMilliliters = _physiology.Blood.LostByDestination[BloodDestination.RightPleural];
        Recalculate(Left); Recalculate(Right);
        float pulmonaryLoss = Math.Clamp(_lesions.Sum(x => x.PulmonaryFunctionLoss) * .5f, 0f, .8f);
        float ventilation = Math.Clamp(((1f - Left.LungCompression) + (1f - Right.LungCompression)) * .5f - pulmonaryLoss, 0f, 1f);
        float pericardial = _physiology.Blood.LostByDestination[BloodDestination.Pericardial];
        float tamponade = Math.Clamp((pericardial - _parameters.TamponadeOnsetMl) / MathF.Max(1f, _parameters.TamponadeCriticalMl - _parameters.TamponadeOnsetMl), 0f, 1f);
        float tensionCirculatoryPenalty = Math.Clamp((Left.IsTension ? Left.PressureKpa / 10f : 0f) + (Right.IsTension ? Right.PressureKpa / 10f : 0f), 0f, .7f);
        float cardiac = Math.Clamp((1f - tamponade * .85f) * (1f - tensionCirculatoryPenalty), 0f, 1f);
        float effectiveVentilation = ventilation * Math.Clamp(NeurologicalVentilationModifier, 0f, 1f);
        float effectiveCardiac = cardiac * Math.Clamp(NeurologicalCardiacModifier, 0f, 1f);
        _physiology.VentilationEffectiveness = effectiveVentilation;
        _physiology.CardiacFunction = effectiveCardiac;
        State = new(Left, Right, effectiveVentilation, effectiveCardiac, pericardial, tamponade);
    }

    private void UpdateCompartment(PleuralCompartment compartment, float dt)
    {
        foreach (ThoracicLesion lesion in _lesions.Where(x => x.Side == compartment.Side))
        {
            compartment.GasMilliliters += lesion.LungLeakMlPerSecond * dt;
            float conductance = lesion.OpenWoundArea * _parameters.OpenWoundConductancePerSecond;
            float sealFactor = compartment.SealState switch { ChestSealState.Effective => 0f, ChestSealState.Vented => .15f, ChestSealState.Partial => .5f, ChestSealState.Blocked => 0f, ChestSealState.Detached => 1f, _ => 1f };
            if (conductance > 0f)
            {
                float exchange = conductance * sealFactor * 1000f * dt;
                compartment.GasMilliliters = lesion.OneWayValve || compartment.SealState == ChestSealState.Blocked
                    ? compartment.GasMilliliters + exchange
                    : MathF.Max(0f, compartment.GasMilliliters - exchange);
            }
        }
        if (compartment.NeedleInPlace) compartment.GasMilliliters = MathF.Max(0f, compartment.GasMilliliters - _parameters.NeedleVentingMlPerSecond * dt);
    }

    private void Recalculate(PleuralCompartment c)
    {
        c.PressureKpa = c.GasMilliliters / _parameters.PleuralComplianceMlPerKpa;
        c.LungCompression = Math.Clamp((c.GasMilliliters + c.BloodMilliliters) / _parameters.LungCompressionVolumeMl, 0f, 1f);
        c.IsTension = c.PressureKpa >= _parameters.TensionPressureKpa;
    }
    private PleuralCompartment Compartment(ThoracicSide side) => side == ThoracicSide.Left ? Left : Right;
}
