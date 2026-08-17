using System;
using System.Numerics;

namespace TacticalSim.Core.Ballistics
{
    /// <summary>
    /// Represents the environmental conditions at a specific point in space.
    /// </summary>
    public struct EnvironmentState
    {
        public Vector3 WindVelocity; // m/s
        public Vector3 Gravity; // m/s^2
        public float AirDensity; // kg/m^3
        public float SpeedOfSound; // m/s
    }

    public interface IEnvironmentModel
    {
        EnvironmentState GetConditionsAt(Vector3 position);
    }

    /// <summary>
    /// Implements the ICAO Standard Atmosphere model.
    /// Assumes position.Y is altitude in meters above sea level.
    /// </summary>
    public class ICAOStandardAtmosphere : IEnvironmentModel
    {
        private readonly Vector3 _baseWind;
        private readonly Vector3 _gravity;

        // Constants for the troposphere (0 to 11,000 meters)
        private const float SeaLevelTemperature = 288.15f; // Kelvin
        private const float SeaLevelPressure = 101325f; // Pascals
        private const float TemperatureLapseRate = -0.0065f; // K/m
        private const float UniversalGasConstant = 8.3144598f; // J/(mol·K)
        private const float MolarMassOfAir = 0.0289644f; // kg/mol
        private const float GravityConstant = 9.80665f; // m/s^2
        private const float SpecificGasConstant = UniversalGasConstant / MolarMassOfAir; // ~287.05 J/(kg·K)
        private const float HeatCapacityRatio = 1.4f; // Gamma for diatomic gas (air)

        public ICAOStandardAtmosphere(Vector3 baseWind, Vector3 gravity)
        {
            _baseWind = baseWind;
            _gravity = gravity;
        }

        public EnvironmentState GetConditionsAt(Vector3 position)
        {
            float altitude = MathF.Max(0, position.Y); // Clamp to sea level minimum for simplified model

            // Calculate temperature (T = T0 + L * h)
            float temperature = SeaLevelTemperature + TemperatureLapseRate * altitude;
            
            // Calculate pressure (P = P0 * (1 + L*h/T0)^(-g*M/(R*L)))
            float exponent = -(GravityConstant * MolarMassOfAir) / (UniversalGasConstant * TemperatureLapseRate);
            float pressure = SeaLevelPressure * MathF.Pow(1.0f + (TemperatureLapseRate * altitude / SeaLevelTemperature), exponent);

            // Calculate density (rho = P / (R_specific * T))
            float density = pressure / (SpecificGasConstant * temperature);

            // Calculate speed of sound (c = sqrt(gamma * R_specific * T))
            float speedOfSound = MathF.Sqrt(HeatCapacityRatio * SpecificGasConstant * temperature);

            return new EnvironmentState
            {
                WindVelocity = _baseWind, // Can be expanded to include wind gradients based on Y
                Gravity = _gravity,
                AirDensity = density,
                SpeedOfSound = speedOfSound
            };
        }
    }
}
