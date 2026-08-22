using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using TacticalSim.Core.Units;
using SimulationTime = TacticalSim.Core.Units.Time;

namespace TacticalSim.Core.Damage.Ballistics;

/// <summary>
/// Central JSON configuration for persisted damage-model contracts. Typed units are
/// written with explicit canonical-unit property names and vectors are written as
/// System.Numerics-compatible x/y/z components.
/// </summary>
public static class DamageModelJson
{
    public static JsonSerializerOptions CreateOptions(bool writeIndented = false)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = writeIndented
        };

        options.Converters.Add(new DamageModelVersionJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new EnergyJsonConverter());
        options.Converters.Add(new DistanceJsonConverter());
        options.Converters.Add(new MassJsonConverter());
        options.Converters.Add(new AreaJsonConverter());
        options.Converters.Add(new SimulationTimeJsonConverter());
        options.Converters.Add(new VolumeJsonConverter());
        options.Converters.Add(new FlowRateJsonConverter());
        options.Converters.Add(new DensityJsonConverter());
        options.Converters.Add(new PressureJsonConverter());
        options.Converters.Add(new Vector3JsonConverter());
        return options;
    }

    private sealed class DamageModelVersionJsonConverter : JsonConverter<DamageModelVersion>
    {
        public override DamageModelVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException("Damage-model version must be a stable string identifier.");

            try
            {
                return DamageModelVersionExtensions.ParseIdentifier(reader.GetString()!);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException("Unknown damage-model version identifier.", exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, DamageModelVersion value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToIdentifier());
    }

    private sealed class EnergyJsonConverter : JsonConverter<Energy>
    {
        public override Energy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Energy.FromJoules(ReadCanonicalValue(ref reader, "joules"));

        public override void Write(Utf8JsonWriter writer, Energy value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "joules", value.Joules);
    }

    private sealed class DistanceJsonConverter : JsonConverter<Distance>
    {
        public override Distance Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Distance.FromMeters(ReadCanonicalValue(ref reader, "meters"));

        public override void Write(Utf8JsonWriter writer, Distance value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "meters", value.Meters);
    }

    private sealed class MassJsonConverter : JsonConverter<Mass>
    {
        public override Mass Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Mass.FromKilograms(ReadCanonicalValue(ref reader, "kilograms"));

        public override void Write(Utf8JsonWriter writer, Mass value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "kilograms", value.Kilograms);
    }

    private sealed class AreaJsonConverter : JsonConverter<Area>
    {
        public override Area Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Area.FromSquareMeters(ReadCanonicalValue(ref reader, "squareMeters"));

        public override void Write(Utf8JsonWriter writer, Area value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "squareMeters", value.SquareMeters);
    }

    private sealed class SimulationTimeJsonConverter : JsonConverter<SimulationTime>
    {
        public override SimulationTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            SimulationTime.FromSeconds(ReadCanonicalValue(ref reader, "seconds"));

        public override void Write(Utf8JsonWriter writer, SimulationTime value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "seconds", value.Seconds);
    }

    private sealed class VolumeJsonConverter : JsonConverter<Volume>
    {
        public override Volume Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Volume.FromCubicMeters(ReadCanonicalValue(ref reader, "cubicMeters"));

        public override void Write(Utf8JsonWriter writer, Volume value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "cubicMeters", value.CubicMeters);
    }

    private sealed class FlowRateJsonConverter : JsonConverter<FlowRate>
    {
        public override FlowRate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            FlowRate.FromCubicMetersPerSecond(ReadCanonicalValue(ref reader, "cubicMetersPerSecond"));

        public override void Write(Utf8JsonWriter writer, FlowRate value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "cubicMetersPerSecond", value.CubicMetersPerSecond);
    }

    private sealed class DensityJsonConverter : JsonConverter<Density>
    {
        public override Density Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Density.FromKilogramsPerCubicMeter(ReadCanonicalValue(ref reader, "kilogramsPerCubicMeter"));

        public override void Write(Utf8JsonWriter writer, Density value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "kilogramsPerCubicMeter", value.KilogramsPerCubicMeter);
    }

    private sealed class PressureJsonConverter : JsonConverter<Pressure>
    {
        public override Pressure Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Pressure.FromPascals(ReadCanonicalValue(ref reader, "pascals"));

        public override void Write(Utf8JsonWriter writer, Pressure value, JsonSerializerOptions options) =>
            WriteCanonicalValue(writer, "pascals", value.Pascals);
    }

    private sealed class Vector3JsonConverter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("A Vector3 must be a JSON object.");

            float? x = null;
            float? y = null;
            float? z = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a Vector3 component name.");

                string component = reader.GetString()!;
                if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
                    throw new JsonException($"Vector3 component '{component}' must be numeric.");

                switch (component)
                {
                    case "x" when x is null:
                        x = reader.GetSingle();
                        break;
                    case "y" when y is null:
                        y = reader.GetSingle();
                        break;
                    case "z" when z is null:
                        z = reader.GetSingle();
                        break;
                    default:
                        throw new JsonException($"Unexpected or duplicate Vector3 component '{component}'.");
                }
            }

            if (x is null || y is null || z is null)
                throw new JsonException("A Vector3 requires x, y, and z components.");

            return new Vector3(x.Value, y.Value, z.Value);
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("z", value.Z);
            writer.WriteEndObject();
        }
    }

    private static float ReadCanonicalValue(ref Utf8JsonReader reader, string propertyName)
    {
        if (reader.TokenType != JsonTokenType.StartObject
            || !reader.Read()
            || reader.TokenType != JsonTokenType.PropertyName
            || reader.GetString() != propertyName
            || !reader.Read()
            || reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"Expected an object containing numeric '{propertyName}'.");
        }

        float value = reader.GetSingle();
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException($"Expected only the canonical '{propertyName}' value.");

        return value;
    }

    private static void WriteCanonicalValue(Utf8JsonWriter writer, string propertyName, float value)
    {
        writer.WriteStartObject();
        writer.WriteNumber(propertyName, value);
        writer.WriteEndObject();
    }
}
