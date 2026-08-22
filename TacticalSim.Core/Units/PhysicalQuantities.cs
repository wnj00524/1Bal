using System;

namespace TacticalSim.Core.Units;

/// <summary>
/// Small, dependency-free value types for quantities that cross the damage-model
/// boundary. Values are stored in the simulation's canonical SI unit.
/// </summary>
public readonly record struct Distance
{
    private readonly float _meters;

    private Distance(float meters) => _meters = Validate(meters, nameof(meters));

    public float Meters => _meters;

    public static Distance FromMeters(float meters) => new(meters);

    public static Distance operator +(Distance left, Distance right) => FromMeters(left.Meters + right.Meters);
    public static Distance operator -(Distance left, Distance right) => FromMeters(left.Meters - right.Meters);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Area
{
    private readonly float _squareMeters;

    private Area(float squareMeters) => _squareMeters = Validate(squareMeters, nameof(squareMeters));

    public float SquareMeters => _squareMeters;

    public static Area FromSquareMeters(float squareMeters) => new(squareMeters);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Volume
{
    private readonly float _cubicMeters;

    private Volume(float cubicMeters) => _cubicMeters = Validate(cubicMeters, nameof(cubicMeters));

    public float CubicMeters => _cubicMeters;
    public float CubicCentimeters => _cubicMeters * 1_000_000f;

    public static Volume FromCubicMeters(float cubicMeters) => new(cubicMeters);
    public static Volume FromCubicCentimeters(float cubicCentimeters) => new(cubicCentimeters / 1_000_000f);

    public static Volume operator +(Volume left, Volume right) => FromCubicMeters(left.CubicMeters + right.CubicMeters);
    public static Volume operator -(Volume left, Volume right) => FromCubicMeters(left.CubicMeters - right.CubicMeters);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Mass
{
    private readonly float _kilograms;

    private Mass(float kilograms) => _kilograms = Validate(kilograms, nameof(kilograms));

    public float Kilograms => _kilograms;
    public float Grams => _kilograms * 1_000f;

    public static Mass FromKilograms(float kilograms) => new(kilograms);
    public static Mass FromGrams(float grams) => new(grams / 1_000f);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Density
{
    private readonly float _kilogramsPerCubicMeter;

    private Density(float kilogramsPerCubicMeter) => _kilogramsPerCubicMeter = Validate(kilogramsPerCubicMeter, nameof(kilogramsPerCubicMeter));

    public float KilogramsPerCubicMeter => _kilogramsPerCubicMeter;

    public static Density FromKilogramsPerCubicMeter(float kilogramsPerCubicMeter) => new(kilogramsPerCubicMeter);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Time
{
    private readonly float _seconds;

    private Time(float seconds) => _seconds = Validate(seconds, nameof(seconds));

    public float Seconds => _seconds;
    public float Milliseconds => _seconds * 1_000f;

    public static Time FromSeconds(float seconds) => new(seconds);
    public static Time FromMilliseconds(float milliseconds) => new(milliseconds / 1_000f);

    public static Time operator +(Time left, Time right) => FromSeconds(left.Seconds + right.Seconds);
    public static Time operator -(Time left, Time right) => FromSeconds(left.Seconds - right.Seconds);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Energy
{
    private readonly float _joules;

    private Energy(float joules) => _joules = Validate(joules, nameof(joules));

    public float Joules => _joules;

    public static Energy FromJoules(float joules) => new(joules);

    public static Energy operator +(Energy left, Energy right) => FromJoules(left.Joules + right.Joules);
    public static Energy operator -(Energy left, Energy right) => FromJoules(left.Joules - right.Joules);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct Pressure
{
    private readonly float _pascals;

    private Pressure(float pascals) => _pascals = Validate(pascals, nameof(pascals));

    public float Pascals => _pascals;
    public float Megapascals => _pascals / 1_000_000f;

    public static Pressure FromPascals(float pascals) => new(pascals);

    /// <summary>Creates a pressure from MPa; the conversion is explicit at the boundary.</summary>
    public static Pressure FromMegapascals(float megapascals) => new(megapascals * 1_000_000f);

    public static Pressure operator +(Pressure left, Pressure right) => FromPascals(left.Pascals + right.Pascals);
    public static Pressure operator -(Pressure left, Pressure right) => FromPascals(left.Pascals - right.Pascals);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

public readonly record struct FlowRate
{
    private readonly float _cubicMetersPerSecond;

    private FlowRate(float cubicMetersPerSecond) => _cubicMetersPerSecond = Validate(cubicMetersPerSecond, nameof(cubicMetersPerSecond));

    public float CubicMetersPerSecond => _cubicMetersPerSecond;
    public float MillilitersPerSecond => _cubicMetersPerSecond * 1_000_000f;
    public float MillilitersPerMinute => MillilitersPerSecond * 60f;

    public static FlowRate FromCubicMetersPerSecond(float cubicMetersPerSecond) => new(cubicMetersPerSecond);
    public static FlowRate FromMillilitersPerSecond(float millilitersPerSecond) => new(millilitersPerSecond / 1_000_000f);
    public static FlowRate FromMillilitersPerMinute(float millilitersPerMinute) => new(millilitersPerMinute / 60f / 1_000_000f);

    public static FlowRate operator +(FlowRate left, FlowRate right) => FromCubicMetersPerSecond(left.CubicMetersPerSecond + right.CubicMetersPerSecond);
    public static FlowRate operator -(FlowRate left, FlowRate right) => FromCubicMetersPerSecond(left.CubicMetersPerSecond - right.CubicMetersPerSecond);

    private static float Validate(float value, string parameterName)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(parameterName, "Unit values must be finite.");
        return value;
    }
}

/// <summary>
/// Conversion helpers used only when values cross a persistence or presentation boundary.
/// Core calculations should retain the typed quantity instead of calling these methods.
/// </summary>
public static class UnitBoundaryConversions
{
    public static float ToSerializedMeters(Distance value) => value.Meters;
    public static Distance FromSerializedMeters(float value) => Distance.FromMeters(value);
    public static float ToSerializedSquareMeters(Area value) => value.SquareMeters;
    public static Area FromSerializedSquareMeters(float value) => Area.FromSquareMeters(value);
    public static float ToSerializedCubicMeters(Volume value) => value.CubicMeters;
    public static Volume FromSerializedCubicMeters(float value) => Volume.FromCubicMeters(value);
    public static float ToSerializedKilograms(Mass value) => value.Kilograms;
    public static Mass FromSerializedKilograms(float value) => Mass.FromKilograms(value);
    public static float ToSerializedKilogramsPerCubicMeter(Density value) => value.KilogramsPerCubicMeter;
    public static Density FromSerializedKilogramsPerCubicMeter(float value) => Density.FromKilogramsPerCubicMeter(value);
    public static float ToSerializedSeconds(Time value) => value.Seconds;
    public static Time FromSerializedSeconds(float value) => Time.FromSeconds(value);
    public static float ToSerializedJoules(Energy value) => value.Joules;
    public static Energy FromSerializedJoules(float value) => Energy.FromJoules(value);
    public static float ToSerializedPascals(Pressure value) => value.Pascals;
    public static Pressure FromSerializedPascals(float value) => Pressure.FromPascals(value);
    public static float ToSerializedCubicMetersPerSecond(FlowRate value) => value.CubicMetersPerSecond;
    public static FlowRate FromSerializedCubicMetersPerSecond(float value) => FlowRate.FromCubicMetersPerSecond(value);

    public static float ToDisplaySquareMillimeters(Area value) => value.SquareMeters * 1_000_000f;
    public static Area FromDisplaySquareMillimeters(float value) => Area.FromSquareMeters(value / 1_000_000f);
    public static float ToDisplayMegapascals(Pressure value) => value.Megapascals;
    public static float ToDisplayCubicCentimeters(Volume value) => value.CubicCentimeters;
    public static Volume FromDisplayCubicCentimeters(float value) => Volume.FromCubicCentimeters(value);
    public static float ToDisplayKilogramsPerCubicMeter(Density value) => value.KilogramsPerCubicMeter;
    public static Density FromDisplayKilogramsPerCubicMeter(float value) => Density.FromKilogramsPerCubicMeter(value);
    public static float ToDisplayMillilitersPerSecond(FlowRate value) => value.MillilitersPerSecond;
    public static FlowRate FromDisplayMillilitersPerSecond(float value) => FlowRate.FromMillilitersPerSecond(value);
    public static float ToDisplayMillilitersPerMinute(FlowRate value) => value.MillilitersPerMinute;
    public static FlowRate FromDisplayMillilitersPerMinute(float value) => FlowRate.FromMillilitersPerMinute(value);
}
